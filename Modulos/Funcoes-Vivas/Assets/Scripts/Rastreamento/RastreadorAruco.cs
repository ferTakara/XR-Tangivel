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

    [Header("Estabilização da Âncora (no espaço do mundo)")]
    [Tooltip("Tempo da média da posição lateral e da rotação (s). Maior = mais firme, mais lento para corrigir.")]
    public float tempoSuavizacaoAncora = 0.5f;
    [Tooltip("Tempo da média da DISTÂNCIA até o cartão (s): a medida mais ruidosa, por isso mais longa.")]
    public float tempoSuavizacaoProfundidade = 1.5f;
    [Tooltip("Deslocamento lateral abaixo disto (m) é tratado como ruído (o cartão está parado na mesa).")]
    public float limiarMovimentoMetros = 0.03f;
    [Tooltip("Variação de distância abaixo disto (m) é tratada como ruído.")]
    public float limiarProfundidadeMetros = 0.15f;
    [Tooltip("Variação de rotação abaixo disto (graus) é tratada como ruído.")]
    public float limiarMovimentoGraus = 8f;
    [Tooltip("Frames seguidos fora dos limiares para aceitar que o cartão foi movido.")]
    public int framesParaMover = 5;

    [Header("Movimento da Câmera")]
    [Tooltip("Acima desta velocidade de giro (graus/s) as medições do cartão 40 são ignoradas e a âncora fica parada.")]
    public float velocidadeAngularMaxGraus = 20f;
    [Tooltip("Acima desta velocidade de deslocamento (m/s) as medições do cartão 40 são ignoradas.")]
    public float velocidadeLinearMax = 0.2f;

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

    // Estabilização da âncora no espaço do MUNDO (o cartão fica parado na mesa)
    private readonly EstabilizadorPoseEstatica estabilizador = new EstabilizadorPoseEstatica();
    private float yawAnterior = 0f;
    private bool primeiroYaw = true;

    // Movimento da câmera entre frames
    private Pose poseCameraAnterior;
    private float tempoFrameAnterior = -1f;
    private float velocidadeAngularCamera; // graus/s
    private float velocidadeLinearCamera;  // m/s

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
        AtualizarVelocidadeCamera(frame.poseCameraMundo);

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

        // 1. Cantos crus, SEM suavizar na imagem. Suavizar na imagem e converter com a pose atual
        //    da câmera fazia o gráfico "escorregar" junto com o movimento da câmera.
        //    A estabilização agora é feita no espaço do mundo, onde o cartão está parado.

        // Câmera girando/andando rápido: a imagem e a pose da câmera não batem exatamente nesse
        // instante. Ignora a medição e mantém a âncora onde está.
        if (CameraEmMovimentoRapido()) return isAncoradoNoMundo;

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

        Vector2 vp0 = ToViewport(c0);
        Vector2 vp1 = ToViewport(c1);
        Vector2 vp2 = ToViewport(c2);
        Vector2 vp3 = ToViewport(c3);

        // 3. Distância Z média (média das 4 arestas = máxima estabilidade)
        float e0 = Vector2.Distance(c0, c1);
        float e1 = Vector2.Distance(c1, c2);
        float e2 = Vector2.Distance(c2, c3);
        float e3 = Vector2.Distance(c3, c0);
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
        if (primeiroYaw) { yawAnterior = yawRaw; primeiroYaw = false; }
        float diffYaw = Mathf.DeltaAngle(yawAnterior, yawRaw);
        yawRaw = yawAnterior + diffYaw;
        yawAnterior = yawRaw;

        // 6. Estabilização no mundo: ruído pequeno vira média lenta (a distância, mais ruidosa,
        //    tem média mais longa); só salta quando o cartão é realmente movido.
        estabilizador.tau = tempoSuavizacaoAncora;
        estabilizador.tauProfundidade = tempoSuavizacaoProfundidade;
        estabilizador.limiarMovimentoMetros = limiarMovimentoMetros;
        estabilizador.limiarProfundidadeMetros = limiarProfundidadeMetros;
        estabilizador.limiarMovimentoGraus = limiarMovimentoGraus;
        estabilizador.framesParaMover = framesParaMover;
        Vector3 direcaoRaio = (posRaw - cameraAR.transform.position).normalized;
        estabilizador.Atualizar(posRaw, yawRaw, Time.time, direcaoRaio);

        Vector3 posFiltrada = estabilizador.Posicao;
        float yawFiltrado = estabilizador.YawGraus;

        // 7. Construir rotação final
        rotacaoAlvo40 = Quaternion.LookRotation(
            new Vector3(Mathf.Sin(yawFiltrado * Mathf.Deg2Rad), 0f, Mathf.Cos(yawFiltrado * Mathf.Deg2Rad)),
            upGrafico);

        // 8. Posição final com offset
        posicaoAlvo40 = posFiltrada + rotacaoAlvo40 * offsetAncoraMarcador;
        return true;
    }

    // ============================================================================================
    // Movimento da câmera
    // ============================================================================================

    private void AtualizarVelocidadeCamera(Pose poseAtual)
    {
        float agora = Time.time;
        if (tempoFrameAnterior >= 0f)
        {
            float dt = Mathf.Max(agora - tempoFrameAnterior, 0.001f);
            velocidadeAngularCamera = Quaternion.Angle(poseCameraAnterior.rotation, poseAtual.rotation) / dt;
            velocidadeLinearCamera = Vector3.Distance(poseCameraAnterior.position, poseAtual.position) / dt;
        }
        poseCameraAnterior = poseAtual;
        tempoFrameAnterior = agora;
    }

    private bool CameraEmMovimentoRapido() =>
        velocidadeAngularCamera > velocidadeAngularMaxGraus || velocidadeLinearCamera > velocidadeLinearMax;

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
        primeiroYaw = true;
        estabilizador.Reset();
    }
}