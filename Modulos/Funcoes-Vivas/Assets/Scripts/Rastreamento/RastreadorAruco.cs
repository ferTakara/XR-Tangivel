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

    // Suporta detecção de múltiplos marcadores simultaneamente na mesma imagem
    [DllImport("MinhaArucoDLL2")]
    private static extern int DetectarMultiplosArucos(
        IntPtr pixels, 
        int width, 
        int height, 
        int channels, 
        int maxMarcadores, 
        int[] outIds, 
        float[] outCorners
    );

    private const int MAX_MARCADORES = 10;
    private int[] bufferIds = new int[MAX_MARCADORES];
    private float[] bufferCantos = new float[MAX_MARCADORES * 8];

    // Variáveis de estado do marcador 40 (Âncora)
    private bool marcador40Detectado = false;
    private Vector3 posicaoAlvo40;
    private Quaternion rotacaoAlvo40;
    private float tempoSemDetectar40 = 0f;
    private const float TEMPO_TOLERANCIA_PERDA = 0.35f; // Mantém posição por 350ms antes de voltar ao modo egocêntrico

    // Filtro Passa-Baixa 2D para estabilizar cantos e remover jitter do cálculo de Z
    private Vector2[] cantosSuavizados40 = new Vector2[4];
    private bool primeiraVez40 = true;

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
        {
            visualizadorFuncao.AtualizarCoeficientes(parametroA.valorAtual, parametroB.valorAtual, parametroC.valorAtual);
        }
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
            // Desativa o seguimento automático da câmera no visualizador para ancorar no marcador
            visualizadorFuncao.seguirCamera = false;

            // Interpola suavemente a posição e rotação até o marcador 40
            float t = Time.deltaTime * suavizacaoAncora;
            visualizadorFuncao.transform.position = Vector3.Lerp(visualizadorFuncao.transform.position, posicaoAlvo40, t);
            visualizadorFuncao.transform.rotation = Quaternion.Slerp(visualizadorFuncao.transform.rotation, rotacaoAlvo40, t);
        }
        else
        {
            tempoSemDetectar40 += Time.deltaTime;
            // Se o marcador 40 não for visto durante a tolerância, volta para o frame egocêntrico
            if (tempoSemDetectar40 >= TEMPO_TOLERANCIA_PERDA)
            {
                visualizadorFuncao.seguirCamera = true;
            }
        }
    }

    unsafe void OnCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
    {
        if (!cameraManager.TryAcquireLatestCpuImage(out var image))
        {
            return;
        }

        var planoY = image.GetPlane(0);
        IntPtr ponteiroPixels = (IntPtr)planoY.data.GetUnsafePtr();
        int channels = planoY.pixelStride; // 4 no Unity Editor e 1 no Celular
        
        try 
        {
            int totalDetectados = DetectarMultiplosArucos(
                ponteiroPixels, 
                image.width, 
                image.height, 
                channels, 
                MAX_MARCADORES, 
                bufferIds, 
                bufferCantos
            );

            bool achouA = false;
            bool achouB = false;
            bool achouC = false;
            bool achou40 = false;

            for (int i = 0; i < totalDetectados; i++)
            {
                int id = bufferIds[i];

                // 8 floats por marcador: (x0, y0, x1, y1, x2, y2, x3, y3)
                int offset = i * 8;
                float x0 = bufferCantos[offset + 0];
                float y0 = bufferCantos[offset + 1];
                float x1 = bufferCantos[offset + 2];
                float y1 = bufferCantos[offset + 3];
                float x2 = bufferCantos[offset + 4];
                float y2 = bufferCantos[offset + 5];
                float x3 = bufferCantos[offset + 6];
                float y3 = bufferCantos[offset + 7];

                float dx = x1 - x0;
                float dy = y1 - y0;
                float anguloAtual = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;

                if (id == parametroA.idAlvo)
                {
                    parametroA.ProcessarRotacao(anguloAtual, modo);
                    achouA = true;
                }
                else if (id == parametroB.idAlvo)
                {
                    parametroB.ProcessarRotacao(anguloAtual, modo);
                    achouB = true;
                }
                else if (id == parametroC.idAlvo)
                {
                    parametroC.ProcessarRotacao(anguloAtual, modo);
                    achouC = true;
                }

                if (id == idAlvoMesa)
                {
                    achou40 = true;
                    ProcessarPosicionamentoMarcador40(x0, y0, x1, y1, x2, y2, x3, y3, image.width, image.height);
                }
            }

            // Atualiza status do marcador 40
            if (achou40)
            {
                marcador40Detectado = true;
                tempoSemDetectar40 = 0f;
            }
            else
            {
                marcador40Detectado = false;
                if (tempoSemDetectar40 >= TEMPO_TOLERANCIA_PERDA)
                {
                    primeiraVez40 = true; // Reseta filtro ao perder marcador definitivamente
                }
            }

            // Se algum marcador específico não esteve visível neste frame, reseta seu delta
            if (!achouA) parametroA.ResetarGiro();
            if (!achouB) parametroB.ResetarGiro();
            if (!achouC) parametroC.ResetarGiro();

            // Atualiza o gráfico com todos os coeficientes calculados simultaneamente!
            if ((achouA || achouB || achouC) && visualizadorFuncao != null)
            {
                visualizadorFuncao.AtualizarCoeficientes(
                    parametroA.valorAtual, 
                    parametroB.valorAtual, 
                    parametroC.valorAtual
                );
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

    private void ProcessarPosicionamentoMarcador40(
        float x0, float y0,
        float x1, float y1,
        float x2, float y2,
        float x3, float y3,
        int imgWidth, int imgHeight)
    {
        if (cameraAR == null) cameraAR = Camera.main;
        if (cameraAR == null) return;

        // 1. Filtro passa-baixa 2D (Anti-Jitter)
        Vector2 c0 = new Vector2(x0, y0);
        Vector2 c1 = new Vector2(x1, y1);
        Vector2 c2 = new Vector2(x2, y2);
        Vector2 c3 = new Vector2(x3, y3);

        if (primeiraVez40)
        {
            cantosSuavizados40[0] = c0;
            cantosSuavizados40[1] = c1;
            cantosSuavizados40[2] = c2;
            cantosSuavizados40[3] = c3;
            primeiraVez40 = false;
        }
        else
        {
            float t = Time.deltaTime * 15.0f;
            cantosSuavizados40[0] = Vector2.Lerp(cantosSuavizados40[0], c0, t);
            cantosSuavizados40[1] = Vector2.Lerp(cantosSuavizados40[1], c1, t);
            cantosSuavizados40[2] = Vector2.Lerp(cantosSuavizados40[2], c2, t);
            cantosSuavizados40[3] = Vector2.Lerp(cantosSuavizados40[3], c3, t);
        }

        // 2. Mapeamento correto de pixels do Sensor (OpenCV) para a Tela do Celular (Viewport Unity)
        // Se a tela for retrato e a câmera for paisagem, precisamos rotacionar as coordenadas 90 graus
        bool isPortrait = Screen.width < Screen.height;
        bool isSensorLandscape = imgWidth > imgHeight;

        Vector2 ToViewport(Vector2 pixel)
        {
            if (isPortrait && isSensorLandscape)
            {
                // Rotaciona 90 graus horário para alinhar com a tela Portrait do celular
                return new Vector2(1.0f - (pixel.y / imgHeight), 1.0f - (pixel.x / imgWidth));
            }
            else
            {
                // Apenas inverte Y (OpenCV é Y pra baixo, Unity Viewport é Y pra cima)
                return new Vector2(pixel.x / imgWidth, 1.0f - (pixel.y / imgHeight));
            }
        }

        Vector2 vp0 = ToViewport(cantosSuavizados40[0]);
        Vector2 vp1 = ToViewport(cantosSuavizados40[1]);
        Vector2 vp2 = ToViewport(cantosSuavizados40[2]);
        Vector2 vp3 = ToViewport(cantosSuavizados40[3]);

        // 3. Obter Raios 3D exatos no Espaço da Câmera (Z=1)
        Vector3 GetCameraRay(Vector2 vp)
        {
            Vector3 worldPt = cameraAR.ViewportToWorldPoint(new Vector3(vp.x, vp.y, 1.0f));
            return cameraAR.transform.InverseTransformPoint(worldPt);
        }

        Vector3 v0 = GetCameraRay(vp0); // Top-Left
        Vector3 v1 = GetCameraRay(vp1); // Top-Right
        Vector3 v2 = GetCameraRay(vp2); // Bottom-Right
        Vector3 v3 = GetCameraRay(vp3); // Bottom-Left

        // 4. Geometria Projetiva: Pontos de Fuga para achar a Rotação PERFEITA
        // Retas são produtos vetoriais de pontos
        Vector3 l_top = Vector3.Cross(v0, v1);
        Vector3 l_bot = Vector3.Cross(v3, v2);
        Vector3 l_left = Vector3.Cross(v3, v0);
        Vector3 l_right = Vector3.Cross(v2, v1);

        // Pontos de fuga
        Vector3 vp_x = Vector3.Cross(l_top, l_bot);
        Vector3 vp_y = Vector3.Cross(l_left, l_right);

        // Direção X e Y do marcador no espaço da Câmera
        Vector3 dirX = (Mathf.Abs(vp_x.z) > 1e-5f) ? (vp_x / vp_x.z).normalized : new Vector3(vp_x.x, vp_x.y, 0).normalized;
        Vector3 dirY = (Mathf.Abs(vp_y.z) > 1e-5f) ? (vp_y / vp_y.z).normalized : new Vector3(vp_y.x, vp_y.y, 0).normalized;

        // Garante a orientação correta (direita e cima na tela)
        if (Vector3.Dot(dirX, v1 - v0) < 0) dirX = -dirX;
        if (Vector3.Dot(dirY, v0 - v3) < 0) dirY = -dirY;

        // Normal do plano do marcador
        Vector3 dirZ = Vector3.Cross(dirX, dirY).normalized;
        if (dirZ.z > 0) dirZ = -dirZ; // Garante que aponta pra câmera

        // 5. Calcular a Distância Exata
        float denom0 = Vector3.Dot(dirZ, v0);
        float denom1 = Vector3.Dot(dirZ, v1);
        if (Mathf.Abs(denom0) < 1e-5f) denom0 = 1e-5f * Mathf.Sign(denom0);
        if (Mathf.Abs(denom1) < 1e-5f) denom1 = 1e-5f * Mathf.Sign(denom1);

        float distRatio = Vector3.Distance(v0 / denom0, v1 / denom1);
        float dotT = tamanhoMarcadorMetros / distRatio;

        // Raio central
        Vector3 diag1 = Vector3.Cross(v0, v2);
        Vector3 diag2 = Vector3.Cross(v1, v3);
        Vector3 centro2D = Vector3.Cross(diag1, diag2);
        Vector3 c_ray = (centro2D / centro2D.z).normalized;

        float denomC = Vector3.Dot(dirZ, c_ray);
        if (Mathf.Abs(denomC) < 1e-5f) denomC = 1e-5f * Mathf.Sign(denomC);

        float z_c = Mathf.Abs(dotT / denomC);
        Vector3 posCameraSpace = c_ray * z_c;

        // 6. Converter para Espaço do Mundo
        Vector3 posMarcadorMundo = cameraAR.transform.TransformPoint(posCameraSpace);
        Vector3 mundoX = cameraAR.transform.TransformDirection(dirX);
        Vector3 mundoY = cameraAR.transform.TransformDirection(dirY);
        Vector3 mundoZ = cameraAR.transform.TransformDirection(dirZ);

        // 7. Configurar a Rotação Final
        Vector3 upGrafico = mundoZ;
        Vector3 forwardGrafico = mundoY;
        Vector3.OrthoNormalize(ref upGrafico, ref forwardGrafico);
        rotacaoAlvo40 = Quaternion.LookRotation(forwardGrafico, upGrafico);

        Vector3 offsetMundo = rotacaoAlvo40 * offsetAncoraMarcador;
        posicaoAlvo40 = posMarcadorMundo + offsetMundo;
    }
}