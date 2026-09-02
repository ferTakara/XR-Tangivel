using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.Collections.LowLevel.Unsafe;

public class RastreadorAruco : MonoBehaviour
{
    public ARCameraManager cameraManager;

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
                Debug.Log($"ACHOU O ARUCO! ID: {idEncontrado}");
                Debug.Log($"Canto superior esquerdo tá no X: {cantos[0]}, Y: {cantos[1]} da tela!");
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