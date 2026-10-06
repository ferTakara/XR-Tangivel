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

    [Header("Conexão com Câmera")]
    public ARCameraManager cameraManager;

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

    void Start()
    {
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

            for (int i = 0; i < totalDetectados; i++)
            {
                int id = bufferIds[i];

                // 8 floats por marcador: (x0, y0, x1, y1, x2, y2, x3, y3)
                int offset = i * 8;
                float x0 = bufferCantos[offset + 0];
                float y0 = bufferCantos[offset + 1];
                float x1 = bufferCantos[offset + 2];
                float y1 = bufferCantos[offset + 3];

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
}