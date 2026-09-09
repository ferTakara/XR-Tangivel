using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.Collections.LowLevel.Unsafe;

public class RastreadorAruco : MonoBehaviour
{
    public ARCameraManager cameraManager;
    public float valorPotenciometro = 0f; 
    private float ultimoAngulo = 0f;
    private bool primeiraVez = true;

    [DllImport("MinhaArucoDLL2")]
    private static extern int DetectarAruco(IntPtr pixels, int width, int height, int channels, float[] outCorners);

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
        // Debug.Log("Recebendo frame..."); // Descomente isso se quiser spammar o console para ver se a câmera tá viva!

        if (!cameraManager.TryAcquireLatestCpuImage(out var image))
        {
            Debug.Log("A câmera tá viva, mas o TryAcquireLatestCpuImage falhou! A Simulação não está enviando os pixels da imagem.");
            return;
        }

        var planoY = image.GetPlane(0);
        float[] cantos = new float[8];

        IntPtr ponteiroPixels = (IntPtr)planoY.data.GetUnsafePtr();
        int channels = planoY.pixelStride; // Será 4 no Unity Editor e 1 no Celular
        
        try 
        {
            // Chama a nova versão da DLL
            int idEncontrado = DetectarAruco(ponteiroPixels, image.width, image.height, channels, cantos);

            if (idEncontrado != -1)
            {
                float x0 = cantos[0];
                float y0 = cantos[1];
                float x1 = cantos[2]; // X do segundo canto
                float y1 = cantos[3]; // Y do segundo canto
                float dx = x1 - x0;
                float dy = y1 - y0;
                float anguloAtual = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                if (primeiraVez)
                {
                    ultimoAngulo = anguloAtual;
                    primeiraVez = false;
                }
                float giro = Mathf.DeltaAngle(ultimoAngulo, anguloAtual);
                valorPotenciometro += giro;

                ultimoAngulo = anguloAtual;
                Debug.Log($"Valor do Potenciômetro: {valorPotenciometro:F1}");
            }
            else
            {
                primeiraVez = true;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Erro ao chamar a DLL do OpenCV: {e.Message}");
        }
        finally
        {
            image.Dispose();
        }
    }
}