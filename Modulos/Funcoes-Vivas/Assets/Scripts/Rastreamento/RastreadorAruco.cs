using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using XRTangivel.Adapters.Mobile;
using XRTangivel.Core;
using XRTangivel.Rastreamento;

/// <summary>
/// Rastreamento ArUco: dials A/B/C (marcadores 42-44) e âncora na mesa (marcador 40).
/// Recebe os frames de um IFonteCameraProvider (ArCoreFonteCamera no celular) e usa o
/// ArucoDetectorService para chamar a DLL. O cálculo da pose do marcador 40 é o mesmo da
/// versão original (tamanho do marcador + FOV da câmera + suavização dos cantos + One Euro).
/// </summary>
public class RastreadorAruco : MonoBehaviour, IParametroProvider, IAncoraMundoProvider
{
    // ============================================================================================
    // Campos públicos do Inspector
    // ============================================================================================

    [Header("Âncora Espacial (ArUco 40)")]
    [Tooltip("ID do marcador ArUco que serve de base física/mesa para ancorar o gráfico")]
    public int idAlvoMesa = 40;

    [Tooltip("Tamanho físico do marcador ArUco impresso (em metros, ex: 0.08 = 8cm)")]
    public float tamanhoMarcadorMetros = 0.08f;

    [Tooltip("Offset 3D relativo ao marcador 40 quando ancorado nele (X, Y, Z)")]
    public Vector3 offsetAncoraMarcador = new Vector3(0f, 0.05f, 0f);

    [Tooltip("Velocidade de suavização do movimento até a âncora do marcador")]
    public float suavizacaoAncora = 10.0f;

    [Header("Conexão com Câmera")]
    [Tooltip("Usado para criar o ArCoreFonteCamera quando nenhuma fonte for indicada")]
    public ARCameraManager cameraManager;
    public Camera cameraAR;
    [Tooltip("Opcional: componente que implementa IFonteCameraProvider (ex.: QuestFonteCamera). " +
             "Se vazio, usa/cria um ArCoreFonteCamera neste objeto.")]
    public MonoBehaviour fonteCameraComponente;

    [Header("Integração com Gráfico")]
    public VisualizadorFuncaoQuadratica visualizadorFuncao;

    [Header("Modo de Rotação Global")]
    public ModoRotacaoAruco modo = ModoRotacaoAruco.PotenciometroContinuo;

    [Header("Controle dos Coeficientes da Função f(x) = ax² + bx + c")]
    [Tooltip("Controla o termo quadrático 'a'")]
    public ParametroAruco parametroA = new ParametroAruco
    {
        nome = "a (Curvatura x²)", idAlvo = 42, valorAtual = 1.0f, sensibilidade = 0.01f,
        limitarValor = true, valorMin = -5.0f, valorMax = 5.0f
    };

    [Tooltip("Controla o termo linear 'b'")]
    public ParametroAruco parametroB = new ParametroAruco
    {
        nome = "b (Inclinação x)", idAlvo = 43, valorAtual = 0.0f, sensibilidade = 0.01f,
        limitarValor = true, valorMin = -5.0f, valorMax = 5.0f
    };

    [Tooltip("Controla o termo independente 'c'")]
    public ParametroAruco parametroC = new ParametroAruco
    {
        nome = "c (Intercepto Y)", idAlvo = 44, valorAtual = 0.0f, sensibilidade = 0.01f,
        limitarValor = true, valorMin = -5.0f, valorMax = 5.0f
    };

    // ============================================================================================
    // Estado
    // ============================================================================================

    /// <summary>IDs vistos no último frame de câmera (usado pelo BotaoTangivelAruco).</summary>
    [HideInInspector]
    public HashSet<int> marcadoresVisiveisNesteFrame = new HashSet<int>();

    // Serviços
    private IFonteCameraProvider fonte;
    private ArucoDetectorService detector;
    private bool assinado;

    // Estado da Âncora (Marcador 40)
    private bool isAncoradoNoMundo = false;
    private Vector3 posicaoAlvo40;
    private Quaternion rotacaoAlvo40;

    // Filtro de cantos 2D
    private Vector2[] cantosSuavizados40 = new Vector2[4];
    private bool primeiraVez40 = true;

    // One Euro Filters para posição e yaw
    private OneEuroFilterVec3 filtroPos;
    private OneEuroFilterFloat filtroYaw;
    private float yawAnterior = 0f;

    // ============================================================================================
    // Ciclo de vida Unity
    // ============================================================================================

    void Start()
    {
        if (cameraAR == null)
            cameraAR = Camera.main;

        // Usa os valores iniciais definidos no Inspector
        parametroA.Inicializar(parametroA.valorAtual);
        parametroB.Inicializar(parametroB.valorAtual);
        parametroC.Inicializar(parametroC.valorAtual);

        if (visualizadorFuncao == null)
        {
            visualizadorFuncao = FindAnyObjectByType<VisualizadorFuncaoQuadratica>();
            if (visualizadorFuncao == null)
            {
                GameObject objVisualizador = new GameObject("Visualizador_Funcao_Quadratica");
                visualizadorFuncao = objVisualizador.AddComponent<VisualizadorFuncaoQuadratica>();
            }
        }

        if (visualizadorFuncao != null)
        {
            // Caminho único: o visualizador LÊ os coeficientes e a pose deste provedor no Update dele.
            visualizadorFuncao.RegistrarProvedorParametros(this);
            visualizadorFuncao.RegistrarProvedorAncora(this);
        }

        // One Euro: minCutoff baixo = suave quando parado, beta baixo = pouca reação a velocidade
        filtroPos = new OneEuroFilterVec3(0.4f, 0.0001f, 1.0f);
        filtroYaw = new OneEuroFilterFloat(0.3f, 0.00005f, 1.0f);

        // Detector só de cantos/IDs: a pose do marcador 40 é calculada aqui, como antes
        detector = new ArucoDetectorService { calcularPose = false };
        fonte = ResolverFonte();
        Assinar();
    }

    void OnEnable() => Assinar();   // no primeiro OnEnable a fonte ainda é null; o Start assina
    void OnDisable() => Desassinar();

    private IFonteCameraProvider ResolverFonte()
    {
        if (fonteCameraComponente is IFonteCameraProvider indicada) return indicada;
        if (fonteCameraComponente != null)
            Debug.LogWarning($"[RastreadorAruco] {fonteCameraComponente.GetType().Name} não implementa IFonteCameraProvider; usando ARCore.");

        var arcore = GetComponent<ArCoreFonteCamera>();
        if (arcore == null) arcore = gameObject.AddComponent<ArCoreFonteCamera>();
        if (cameraManager != null) arcore.DefinirCameraManager(cameraManager);
        return arcore;
    }

    private void Assinar()
    {
        if (assinado || fonte == null) return;
        fonte.OnFrame += AoReceberFrame;
        assinado = true;
    }

    private void Desassinar()
    {
        if (!assinado) return;
        if (fonte != null) fonte.OnFrame -= AoReceberFrame;
        assinado = false;
    }

    // Obs.: este componente NÃO move o gráfico. Ele só calcula a pose alvo
    // (IAncoraMundoProvider) e o VisualizadorFuncaoQuadratica é o único dono do transform.
    // O duplo toque para desancorar fica no GestosToqueMobile.

    // ============================================================================================
    // Processamento de cada frame da câmera
    // ============================================================================================

    private void AoReceberFrame(FrameCamera frame)
    {
        if (detector == null) return;

        try
        {
            IReadOnlyList<MarcadorDetectado> marcadores = detector.Detectar(frame);

            bool achouA = false, achouB = false, achouC = false, achou40 = false;
            marcadoresVisiveisNesteFrame.Clear();

            for (int i = 0; i < marcadores.Count; i++)
            {
                MarcadorDetectado m = marcadores[i];
                marcadoresVisiveisNesteFrame.Add(m.id);

                float anguloAtual = m.AnguloImagemGraus;

                if (m.id == parametroA.idAlvo) { parametroA.ProcessarRotacao(anguloAtual, modo); achouA = true; }
                else if (m.id == parametroB.idAlvo) { parametroB.ProcessarRotacao(anguloAtual, modo); achouB = true; }
                else if (m.id == parametroC.idAlvo) { parametroC.ProcessarRotacao(anguloAtual, modo); achouC = true; }

                if (m.id == idAlvoMesa)
                {
                    // Só conta como "achou" se a pose foi realmente calculada neste frame
                    achou40 = ProcessarPosicionamentoMarcador40(m.c0, m.c1, m.c2, m.c3, frame.largura, frame.altura);
                }
            }

            if (achou40)
            {
                isAncoradoNoMundo = true;
            }

            if (!achouA) parametroA.ResetarGiro();
            if (!achouB) parametroB.ResetarGiro();
            if (!achouC) parametroC.ResetarGiro();
        }
        catch (Exception e)
        {
            Debug.LogError($"Erro ao processar ArUco ou DLL: {e.Message}");
        }
    }

    // ============================================================================================
    // Posicionamento do Gráfico sobre o ArUco 40
    //
    // ABORDAGEM: Gravidade (IMU) + Yaw (Pixels) + One Euro Filter
    //
    // A inclinação (tilt) vem do giroscópio/acelerômetro do celular via ARFoundation,
    // que é perfeitamente estável. Apenas o yaw (rotação horizontal) é extraído dos pixels.
    // Isso elimina ~90% do jitter porque o tilt (componente mais ruidoso) não depende mais
    // de cálculos frágeis de profundidade por canto.
    // ============================================================================================

    /// <returns>true se posicaoAlvo40/rotacaoAlvo40 foram atualizadas com uma pose válida.</returns>
    private bool ProcessarPosicionamentoMarcador40(
        Vector2 c0, Vector2 c1, Vector2 c2, Vector2 c3,
        int imgWidth, int imgHeight)
    {
        if (cameraAR == null) cameraAR = Camera.main;
        if (cameraAR == null) return false;

        // 1. Suavizar cantos 2D (deadzone adaptativa)

        if (primeiraVez40)
        {
            cantosSuavizados40[0] = c0; cantosSuavizados40[1] = c1;
            cantosSuavizados40[2] = c2; cantosSuavizados40[3] = c3;
            primeiraVez40 = false;
        }
        else
        {
            for (int i = 0; i < 4; i++)
            {
                Vector2 cNovo = i == 0 ? c0 : i == 1 ? c1 : i == 2 ? c2 : c3;
                float distPx = Vector2.Distance(cantosSuavizados40[i], cNovo);
                // < 3px = ruído da OpenCV → congelar; > 3px = movimento real → acompanhar
                float suavidade = distPx < 3.0f ? 1.0f : 12.0f;
                cantosSuavizados40[i] = Vector2.Lerp(cantosSuavizados40[i], cNovo, Time.deltaTime * suavidade);
            }
        }

        // 2. Converter pixels do sensor (OpenCV) → Viewport do Unity
        bool isPortrait = Screen.width < Screen.height;
        bool isSensorLandscape = imgWidth > imgHeight;

        Vector2 ToViewport(Vector2 pixel)
        {
            if (isPortrait && isSensorLandscape)
                return new Vector2(1.0f - (pixel.y / imgHeight), 1.0f - (pixel.x / imgWidth));
            else
                return new Vector2(pixel.x / imgWidth, 1.0f - (pixel.y / imgHeight));
        }

        Vector2 vp0 = ToViewport(cantosSuavizados40[0]);
        Vector2 vp1 = ToViewport(cantosSuavizados40[1]);
        Vector2 vp2 = ToViewport(cantosSuavizados40[2]);
        Vector2 vp3 = ToViewport(cantosSuavizados40[3]);

        // 3. Distância Z média (média das 4 arestas = máxima estabilidade)
        float e0 = Vector2.Distance(cantosSuavizados40[0], cantosSuavizados40[1]);
        float e1 = Vector2.Distance(cantosSuavizados40[1], cantosSuavizados40[2]);
        float e2 = Vector2.Distance(cantosSuavizados40[2], cantosSuavizados40[3]);
        float e3 = Vector2.Distance(cantosSuavizados40[3], cantosSuavizados40[0]);
        float avgEdge = (e0 + e1 + e2 + e3) * 0.25f;
        if (avgEdge < 8f) return false; // marcador pequeno/longe demais: pose não confiável

        float fovRad = cameraAR.fieldOfView * Mathf.Deg2Rad;
        float focalPx = (imgHeight * 0.5f) / Mathf.Tan(fovRad * 0.5f);
        float distZ = Mathf.Clamp((focalPx * tamanhoMarcadorMetros) / avgEdge, 0.1f, 5.0f);

        // 4. Posição 3D do centro do marcador
        Vector2 vpCentro = (vp0 + vp1 + vp2 + vp3) * 0.25f;
        Vector3 posRaw = cameraAR.ViewportToWorldPoint(new Vector3(vpCentro.x, vpCentro.y, distZ));

        // 5. ROTAÇÃO: Gravidade para tilt + Pixels apenas para yaw
        Vector3 upGrafico = Vector3.up; // Vem do IMU (giroscópio) — sem ruído

        // Projetar cantos no mundo com mesmo Z para calcular a direção "direita" do marcador
        Vector3 p0w = cameraAR.ViewportToWorldPoint(new Vector3(vp0.x, vp0.y, distZ));
        Vector3 p1w = cameraAR.ViewportToWorldPoint(new Vector3(vp1.x, vp1.y, distZ));
        Vector3 p2w = cameraAR.ViewportToWorldPoint(new Vector3(vp2.x, vp2.y, distZ));
        Vector3 p3w = cameraAR.ViewportToWorldPoint(new Vector3(vp3.x, vp3.y, distZ));

        // Direita do marcador (média topo + fundo para estabilidade)
        Vector3 direita = ((p1w - p0w) + (p2w - p3w)) * 0.5f;
        direita.y = 0f; // Projetar no plano horizontal
        if (direita.sqrMagnitude < 0.0001f)
        {
            direita = cameraAR.transform.right;
            direita.y = 0f;
        }
        direita.Normalize();

        // Forward = perpendicular à "direita" no plano horizontal
        Vector3 forwardGrafico = Vector3.Cross(direita, upGrafico).normalized;

        // Extrair yaw bruto e tratar descontinuidade angular (-180/+180)
        float yawRaw = Mathf.Atan2(forwardGrafico.x, forwardGrafico.z) * Mathf.Rad2Deg;
        float diffYaw = Mathf.DeltaAngle(yawAnterior, yawRaw);
        yawRaw = yawAnterior + diffYaw;
        yawAnterior = yawRaw;

        // 6. Aplicar One Euro Filter (mata o jitter residual)
        float tempo = Time.time;
        Vector3 posFiltrada = filtroPos.Filter(posRaw, tempo);
        float yawFiltrado = filtroYaw.Filter(yawRaw, tempo);

        // 7. Construir rotação final
        rotacaoAlvo40 = Quaternion.LookRotation(
            new Vector3(Mathf.Sin(yawFiltrado * Mathf.Deg2Rad), 0f, Mathf.Cos(yawFiltrado * Mathf.Deg2Rad)),
            upGrafico);

        // 8. Posição final com offset
        posicaoAlvo40 = posFiltrada + rotacaoAlvo40 * offsetAncoraMarcador;
        return true;
    }

    // ============================================================================================
    // Implementação das Interfaces Multiplataforma (IParametroProvider e IAncoraMundoProvider)
    // ============================================================================================

    public float CoeficienteA => parametroA != null ? parametroA.valorAtual : 1.0f;
    public float CoeficienteB => parametroB != null ? parametroB.valorAtual : 0.0f;
    public float CoeficienteC => parametroC != null ? parametroC.valorAtual : 0.0f;
    public bool EstaAtivo => this.enabled && gameObject.activeInHierarchy;

    public bool EstaAncorado => isAncoradoNoMundo;

    public bool TentarObterPose(out Vector3 posicao, out Quaternion rotacao)
    {
        posicao = posicaoAlvo40;
        rotacao = rotacaoAlvo40;
        return isAncoradoNoMundo;
    }

    public void Desancorar()
    {
        isAncoradoNoMundo = false;
        if (visualizadorFuncao != null) visualizadorFuncao.seguirCamera = true;
        primeiraVez40 = true;
        if (filtroPos != null) filtroPos.Reset();
        if (filtroYaw != null) filtroYaw.Reset();
    }
}