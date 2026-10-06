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
        if (cameraAR == null)
            cameraAR = Camera.main;

        if (cameraAR == null) return;

        // 1. Centro do marcador na imagem
        float centroX = (x0 + x1 + x2 + x3) * 0.25f;
        float centroY = (y0 + y1 + y2 + y3) * 0.25f;

        float normX = centroX / (float)imgWidth;
        float normY = 1.0f - (centroY / (float)imgHeight);

        // 2. Estima a distância óptica Z de CADA CANTO até a câmera baseado nas larguras/alturas
        float larguraTop = Vector2.Distance(new Vector2(x0, y0), new Vector2(x1, y1));
        float larguraBottom = Vector2.Distance(new Vector2(x3, y3), new Vector2(x2, y2));
        float alturaLeft = Vector2.Distance(new Vector2(x0, y0), new Vector2(x3, y3));
        float alturaRight = Vector2.Distance(new Vector2(x1, y1), new Vector2(x2, y2));

        float fovRad = cameraAR.fieldOfView * Mathf.Deg2Rad;
        float focalLengthPixels = (imgHeight * 0.5f) / Mathf.Tan(fovRad * 0.5f);
        float constanteZ = focalLengthPixels * tamanhoMarcadorMetros;

        // Aproximação do tamanho aparente local para cada canto (média das arestas conectadas)
        float tam0 = (larguraTop + alturaLeft) * 0.5f;
        float tam1 = (larguraTop + alturaRight) * 0.5f;
        float tam2 = (larguraBottom + alturaRight) * 0.5f;
        float tam3 = (larguraBottom + alturaLeft) * 0.5f;

        if (tam0 < 5f || tam1 < 5f || tam2 < 5f || tam3 < 5f) return;

        float z0 = Mathf.Clamp(constanteZ / tam0, 0.15f, 4.0f);
        float z1 = Mathf.Clamp(constanteZ / tam1, 0.15f, 4.0f);
        float z2 = Mathf.Clamp(constanteZ / tam2, 0.15f, 4.0f);
        float z3 = Mathf.Clamp(constanteZ / tam3, 0.15f, 4.0f);

        // 3. Posição 3D dos cantos no espaço do mundo usando as profundidades individuais
        Vector3 p0Mundo = cameraAR.ViewportToWorldPoint(new Vector3(x0 / imgWidth, 1.0f - (y0 / imgHeight), z0));
        Vector3 p1Mundo = cameraAR.ViewportToWorldPoint(new Vector3(x1 / imgWidth, 1.0f - (y1 / imgHeight), z1));
        Vector3 p2Mundo = cameraAR.ViewportToWorldPoint(new Vector3(x2 / imgWidth, 1.0f - (y2 / imgHeight), z2));
        Vector3 p3Mundo = cameraAR.ViewportToWorldPoint(new Vector3(x3 / imgWidth, 1.0f - (y3 / imgHeight), z3));

        // Posição central média em 3D
        Vector3 posMarcadorMundo = (p0Mundo + p1Mundo + p2Mundo + p3Mundo) * 0.25f;

        // 4. Determina os vetores direcionais 3D do plano do marcador no mundo:
        // Canto 0 -> 1 = X do marcador (Direita)
        // Canto 3 -> 0 = Y do marcador na folha (Cima)
        Vector3 marcadorDireita = (p1Mundo - p0Mundo).normalized; 
        Vector3 marcadorCimaFolha = (p0Mundo - p3Mundo).normalized; 

        // Vetor Normal perpendicular ao plano do ArUco (saindo da folha / mesa para fora):
        Vector3 normalPerpendicular = Vector3.Cross(marcadorDireita, marcadorCimaFolha).normalized;

        // Garante que a normal aponte no sentido da câmera (saindo do papel em direção a quem observa)
        Vector3 direcaoParaCamera = (cameraAR.transform.position - posMarcadorMundo).normalized;
        if (Vector3.Dot(normalPerpendicular, direcaoParaCamera) < 0f)
        {
            normalPerpendicular = -normalPerpendicular;
        }

        // Para o gráfico:
        // Queremos que o eixo Y LOCAL do gráfico seja perpendicular ao ArUco (upGrafico)
        // E o eixo Z LOCAL seja "para cima" na folha (forwardGrafico)
        Vector3 upGrafico = normalPerpendicular;
        Vector3 forwardGrafico = marcadorCimaFolha;

        // Orto-normaliza o forward em relação ao up
        if (Mathf.Abs(Vector3.Dot(upGrafico, forwardGrafico)) > 0.99f)
        {
            forwardGrafico = Vector3.Cross(upGrafico, marcadorDireita).normalized;
        }
        else
        {
            Vector3.OrthoNormalize(ref upGrafico, ref forwardGrafico);
        }

        // Calcula a rotação 3D que orienta o objeto
        rotacaoAlvo40 = Quaternion.LookRotation(forwardGrafico, upGrafico);

        // Aplica o offset 3D no espaço local da orientação do marcador
        Vector3 offsetMundo = (rotacaoAlvo40 * offsetAncoraMarcador);
        posicaoAlvo40 = posMarcadorMundo + offsetMundo;
    }
}