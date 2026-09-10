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

    [Header("Conexão com Câmera")]
    public ARCameraManager cameraManager;

    [Header("Configuração do ArUco")]
    [Tooltip("ID do ArUco alvo para controlar a função quadrática")]
    public int idAlvo = 42;

    [Header("Integração com Gráfico Quadrático")]
    [Tooltip("Referência ao visualizador da função f(x) = a * x²")]
    public VisualizadorFuncaoQuadratica visualizadorFuncao;

    [Header("Ajuste da Rotação -> Escalar 'a'")]
    public ModoRotacao modo = ModoRotacao.PotenciometroContinuo;

    [Tooltip("Sensibilidade que converte graus em valor escalar 'a' (ex: 0.01 = 100° equivale a 1.0)")]
    public float sensibilidade = 0.01f;

    [Tooltip("Limita o valor mínimo e máximo de 'a'")]
    public bool limitarValor = true;
    public float valorMin = -5.0f;
    public float valorMax = 5.0f;

    [Header("Valores em Tempo Real (Leitura)")]
    public float valorPotenciometro = 0f;
    public float coeficienteA = 0f;

    private float ultimoAngulo = 0f;
    private bool primeiraVez = true;

    [DllImport("MinhaArucoDLL2")]
    private static extern int DetectarAruco(IntPtr pixels, int width, int height, int channels, float[] outCorners);

    void Start()
    {
        // Se não foi associado no Inspector, procura na cena ou cria um automaticamente
        if (visualizadorFuncao == null)
        {
            visualizadorFuncao = FindAnyObjectByType<VisualizadorFuncaoQuadratica>();
            if (visualizadorFuncao == null)
            {
                GameObject objVisualizador = new GameObject("Visualizador_Funcao_Quadratica");
                visualizadorFuncao = objVisualizador.AddComponent<VisualizadorFuncaoQuadratica>();
            }
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
        float[] cantos = new float[8];

        IntPtr ponteiroPixels = (IntPtr)planoY.data.GetUnsafePtr();
        int channels = planoY.pixelStride; // 4 no Unity Editor e 1 no Celular
        
        try 
        {
            int idEncontrado = DetectarAruco(ponteiroPixels, image.width, image.height, channels, cantos);

            // Verifica especificamente o ArUco alvo (ID 42)
            if (idEncontrado == idAlvo)
            {
                // Cantos do marcador:
                // canto 0: (cantos[0], cantos[1])
                // canto 1: (cantos[2], cantos[3])
                float x0 = cantos[0];
                float y0 = cantos[1];
                float x1 = cantos[2];
                float y1 = cantos[3];

                float dx = x1 - x0;
                float dy = y1 - y0;

                // Ângulo de rotação do marcador em graus
                float anguloAtual = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;

                if (modo == ModoRotacao.PotenciometroContinuo)
                {
                    if (primeiraVez)
                    {
                        ultimoAngulo = anguloAtual;
                        primeiraVez = false;
                    }

                    // Delta relativo entre frames evita saltos bruscos na transição de -180° / +180°
                    float giro = Mathf.DeltaAngle(ultimoAngulo, anguloAtual);
                    valorPotenciometro += giro;
                    ultimoAngulo = anguloAtual;

                    coeficienteA = valorPotenciometro * sensibilidade;
                }
                else // AnguloAbsoluto
                {
                    coeficienteA = anguloAtual * sensibilidade;
                }

                if (limitarValor)
                {
                    coeficienteA = Mathf.Clamp(coeficienteA, valorMin, valorMax);
                    if (modo == ModoRotacao.PotenciometroContinuo)
                        valorPotenciometro = coeficienteA / sensibilidade;
                }

                // Atualiza o gráfico em tempo real
                if (visualizadorFuncao != null)
                {
                    visualizadorFuncao.AtualizarCoeficienteA(coeficienteA);
                }

                Debug.Log($"[ArUco {idAlvo}] Ângulo: {anguloAtual:F1}° | Coeficiente 'a': {coeficienteA:F2} | f(x) = {coeficienteA:F2}x²");
            }
            else
            {
                primeiraVez = true;
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