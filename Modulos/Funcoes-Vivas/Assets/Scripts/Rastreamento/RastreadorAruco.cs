using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.Collections.LowLevel.Unsafe;

public class RastreadorAruco : MonoBehaviour
{
    public enum ModoRotacao
    {
        PotenciometroContinuo, // Acumula giros como um botão físico giratório (permite várias voltas)
        AnguloAbsoluto         // O ângulo direto do marcador define o valor (-180° a +180°)
    }

    [System.Serializable]
    public class ParametroAruco
    {
        public string nome = "Parâmetro";
        public int idAlvo = 42;
        public float valorAtual = 0f;
        public float sensibilidade = 0.01f;
        public bool limitarValor = true;
        public float valorMin = -5.0f;
        public float valorMax = 5.0f;

        [HideInInspector] public float valorPotenciometro = 0f;
        [HideInInspector] public float ultimoAngulo = 0f;
        [HideInInspector] public bool primeiraVez = true;

        public void Inicializar(float valorInicial)
        {
            valorAtual = valorInicial;
            float sens = Mathf.Abs(sensibilidade) > 0.0001f ? sensibilidade : 0.01f;
            valorPotenciometro = valorAtual / sens;
            primeiraVez = true;
        }

        public void ProcessarRotacao(float anguloAtual, ModoRotacao modo)
        {
            if (modo == ModoRotacao.PotenciometroContinuo)
            {
                if (primeiraVez)
                {
                    ultimoAngulo = anguloAtual;
                    primeiraVez = false;
                }

                float giro = Mathf.DeltaAngle(ultimoAngulo, anguloAtual);
                valorPotenciometro += giro;
                ultimoAngulo = anguloAtual;
                valorAtual = valorPotenciometro * sensibilidade;
            }
            else // AnguloAbsoluto
            {
                valorAtual = anguloAtual * sensibilidade;
            }

            if (limitarValor)
            {
                valorAtual = Mathf.Clamp(valorAtual, valorMin, valorMax);
                if (modo == ModoRotacao.PotenciometroContinuo)
                {
                    float sens = Mathf.Abs(sensibilidade) > 0.0001f ? sensibilidade : 0.01f;
                    valorPotenciometro = valorAtual / sens;
                }
            }
        }

        public void ResetarGiro()
        {
            primeiraVez = true;
        }
    }

    // ============================================================================================
    // One Euro Filter (CHI 2012) — padrão ouro para estabilização de tracking em AR/VR
    // Filtra forte quando parado (mata jitter), solta quando se move (sem lag)
    // ============================================================================================
    private class OneEuroFilterFloat
    {
        float minCutoff, beta, dCutoff;
        float prevValue, prevDeriv, prevTime;
        bool started;

        public OneEuroFilterFloat(float minCutoff = 0.3f, float beta = 0.00001f, float dCutoff = 1.0f)
        {
            this.minCutoff = minCutoff;
            this.beta = beta;
            this.dCutoff = dCutoff;
            started = false;
        }

        float Alpha(float cutoff, float dt)
        {
            float tau = 1.0f / (2f * Mathf.PI * cutoff);
            return 1.0f / (1.0f + tau / Mathf.Max(dt, 0.001f));
        }

        public float Filter(float value, float timestamp)
        {
            if (!started)
            {
                prevValue = value;
                prevDeriv = 0f;
                prevTime = timestamp;
                started = true;
                return value;
            }

            float dt = timestamp - prevTime;
            if (dt <= 0f) dt = 1f / 30f;
            prevTime = timestamp;

            float deriv = (value - prevValue) / dt;
            float aD = Alpha(dCutoff, dt);
            deriv = aD * deriv + (1f - aD) * prevDeriv;

            float cutoff = minCutoff + beta * Mathf.Abs(deriv);
            float a = Alpha(cutoff, dt);
            float filtered = a * value + (1f - a) * prevValue;

            prevValue = filtered;
            prevDeriv = deriv;
            return filtered;
        }

        public void Reset() { started = false; }
    }

    private class OneEuroFilterVec3
    {
        OneEuroFilterFloat fx, fy, fz;

        public OneEuroFilterVec3(float minCutoff = 0.3f, float beta = 0.00001f, float dCutoff = 1.0f)
        {
            fx = new OneEuroFilterFloat(minCutoff, beta, dCutoff);
            fy = new OneEuroFilterFloat(minCutoff, beta, dCutoff);
            fz = new OneEuroFilterFloat(minCutoff, beta, dCutoff);
        }

        public Vector3 Filter(Vector3 v, float t)
        {
            return new Vector3(fx.Filter(v.x, t), fy.Filter(v.y, t), fz.Filter(v.z, t));
        }

        public void Reset() { fx.Reset(); fy.Reset(); fz.Reset(); }
    }

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
    public ARCameraManager cameraManager;
    public Camera cameraAR;

    [Header("Integração com Gráfico")]
    public VisualizadorFuncaoQuadratica visualizadorFuncao;

    [Header("Modo de Rotação Global")]
    public ModoRotacao modo = ModoRotacao.PotenciometroContinuo;

    [Header("Controle dos Coeficientes da Função f(x) = ax² + bx + c")]
    [Tooltip("Controla o termo quadrático 'a'")]
    public ParametroAruco parametroA = new ParametroAruco 
    { 
        nome = "a (Curvatura x²)", 
        idAlvo = 42, 
        valorAtual = 1.0f, 
        sensibilidade = 0.01f, 
        limitarValor = true, 
        valorMin = -5.0f, 
        valorMax = 5.0f 
    };

    [Tooltip("Controla o termo linear 'b'")]
    public ParametroAruco parametroB = new ParametroAruco 
    { 
        nome = "b (Inclinação x)", 
        idAlvo = 43, 
        valorAtual = 0.0f, 
        sensibilidade = 0.01f, 
        limitarValor = true, 
        valorMin = -5.0f, 
        valorMax = 5.0f 
    };

    [Tooltip("Controla o termo independente 'c'")]
    public ParametroAruco parametroC = new ParametroAruco 
    { 
        nome = "c (Intercepto Y)", 
        idAlvo = 44, 
        valorAtual = 0.0f, 
        sensibilidade = 0.01f, 
        limitarValor = true, 
        valorMin = -5.0f, 
        valorMax = 5.0f 
    };

    // ============================================================================================
    // Campos privados
    // ============================================================================================

    [DllImport("MinhaArucoDLL2")]
    private static extern int DetectarMultiplosArucos(
        IntPtr pixels, int width, int height, int channels,
        int maxMarcadores, int[] outIds, float[] outCorners
    );

    private const int MAX_MARCADORES = 10;
    private int[] bufferIds = new int[MAX_MARCADORES];
    private float[] bufferCantos = new float[MAX_MARCADORES * 8];

    // Estado do marcador 40
    private bool marcador40Detectado = false;
    private Vector3 posicaoAlvo40;
    private Quaternion rotacaoAlvo40;
    private float tempoSemDetectar40 = 0f;
    private const float TEMPO_TOLERANCIA_PERDA = 0.35f;

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

        parametroA.Inicializar(1.0f);
        parametroB.Inicializar(0.0f);
        parametroC.Inicializar(0.0f);

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
            visualizadorFuncao.AtualizarCoeficientes(parametroA.valorAtual, parametroB.valorAtual, parametroC.valorAtual);

        // One Euro: minCutoff baixo = suave quando parado, beta baixo = pouca reação a velocidade
        filtroPos = new OneEuroFilterVec3(0.4f, 0.0001f, 1.0f);
        filtroYaw = new OneEuroFilterFloat(0.3f, 0.00005f, 1.0f);
    }

    void OnEnable()
    {
        if (cameraManager != null)
            cameraManager.frameReceived += OnCameraFrameReceived;
    }

    void OnDisable()
    {
        if (cameraManager != null)
            cameraManager.frameReceived -= OnCameraFrameReceived;
    }

    void Update()
    {
        if (visualizadorFuncao == null) return;

        if (marcador40Detectado)
        {
            visualizadorFuncao.seguirCamera = false;

            // Deadzone: se a diferença é minúscula, reduz a velocidade para "congelar"
            float dist = Vector3.Distance(visualizadorFuncao.transform.position, posicaoAlvo40);
            float ang = Quaternion.Angle(visualizadorFuncao.transform.rotation, rotacaoAlvo40);

            float velPos = dist < 0.01f ? 2.0f : suavizacaoAncora;
            float velRot = ang < 2.0f ? 2.0f : suavizacaoAncora;

            visualizadorFuncao.transform.position = Vector3.Lerp(
                visualizadorFuncao.transform.position, posicaoAlvo40, Time.deltaTime * velPos);
            visualizadorFuncao.transform.rotation = Quaternion.Slerp(
                visualizadorFuncao.transform.rotation, rotacaoAlvo40, Time.deltaTime * velRot);
        }
        else
        {
            tempoSemDetectar40 += Time.deltaTime;
            if (tempoSemDetectar40 >= TEMPO_TOLERANCIA_PERDA)
            {
                visualizadorFuncao.seguirCamera = true;
                primeiraVez40 = true;
                filtroPos.Reset();
                filtroYaw.Reset();
            }
        }
    }

    // ============================================================================================
    // Processamento de Frame da Câmera AR
    // ============================================================================================

    unsafe void OnCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
    {
        if (!cameraManager.TryAcquireLatestCpuImage(out var image))
            return;

        var planoY = image.GetPlane(0);
        IntPtr ponteiroPixels = (IntPtr)planoY.data.GetUnsafePtr();
        int channels = planoY.pixelStride;
        
        try 
        {
            int totalDetectados = DetectarMultiplosArucos(
                ponteiroPixels, image.width, image.height, channels,
                MAX_MARCADORES, bufferIds, bufferCantos
            );

            bool achouA = false, achouB = false, achouC = false, achou40 = false;

            for (int i = 0; i < totalDetectados; i++)
            {
                int id = bufferIds[i];
                int offset = i * 8;
                float x0 = bufferCantos[offset + 0], y0 = bufferCantos[offset + 1];
                float x1 = bufferCantos[offset + 2], y1 = bufferCantos[offset + 3];
                float x2 = bufferCantos[offset + 4], y2 = bufferCantos[offset + 5];
                float x3 = bufferCantos[offset + 6], y3 = bufferCantos[offset + 7];

                float anguloAtual = Mathf.Atan2(y1 - y0, x1 - x0) * Mathf.Rad2Deg;

                if (id == parametroA.idAlvo) { parametroA.ProcessarRotacao(anguloAtual, modo); achouA = true; }
                else if (id == parametroB.idAlvo) { parametroB.ProcessarRotacao(anguloAtual, modo); achouB = true; }
                else if (id == parametroC.idAlvo) { parametroC.ProcessarRotacao(anguloAtual, modo); achouC = true; }

                if (id == idAlvoMesa)
                {
                    achou40 = true;
                    ProcessarPosicionamentoMarcador40(x0, y0, x1, y1, x2, y2, x3, y3, image.width, image.height);
                }
            }

            if (achou40) { marcador40Detectado = true; tempoSemDetectar40 = 0f; }
            else         { marcador40Detectado = false; }

            if (!achouA) parametroA.ResetarGiro();
            if (!achouB) parametroB.ResetarGiro();
            if (!achouC) parametroC.ResetarGiro();

            if ((achouA || achouB || achouC) && visualizadorFuncao != null)
            {
                visualizadorFuncao.AtualizarCoeficientes(
                    parametroA.valorAtual, parametroB.valorAtual, parametroC.valorAtual);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Erro ao processar ArUco ou DLL: {e.Message}");
        }
        finally
        {
            image.Dispose();
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

    private void ProcessarPosicionamentoMarcador40(
        float x0, float y0, float x1, float y1,
        float x2, float y2, float x3, float y3,
        int imgWidth, int imgHeight)
    {
        if (cameraAR == null) cameraAR = Camera.main;
        if (cameraAR == null) return;

        // 1. Suavizar cantos 2D (deadzone adaptativa)
        Vector2 c0 = new Vector2(x0, y0), c1 = new Vector2(x1, y1);
        Vector2 c2 = new Vector2(x2, y2), c3 = new Vector2(x3, y3);

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
        if (avgEdge < 8f) return;

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
    }
}