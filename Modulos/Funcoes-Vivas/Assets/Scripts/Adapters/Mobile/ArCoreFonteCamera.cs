using System;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using XRTangivel.Core;

namespace XRTangivel.Adapters.Mobile
{
    /// <summary>
    /// Fonte de câmera do celular (ARCore via AR Foundation).
    /// Entrega o plano Y da imagem da CPU com os intrínsecos reais da câmera (TryGetIntrinsics)
    /// e a pose da câmera, para o detector calcular a posição 3D dos marcadores sem depender
    /// do FOV da tela nem do recorte da imagem de fundo.
    /// </summary>
    public class ArCoreFonteCamera : MonoBehaviour, IFonteCameraProvider
    {
        [Tooltip("ARCameraManager da AR Camera. Se vazio, procura na cena.")]
        public ARCameraManager cameraManager;

        public event Action<FrameCamera> OnFrame;
        public bool EstaAtiva => isActiveAndEnabled && cameraManager != null;

        private bool assinado;
        private bool avisouSemIntrinsecos;

        void Awake()
        {
            if (cameraManager == null) cameraManager = FindAnyObjectByType<ARCameraManager>();
        }

        void OnEnable() => Assinar();
        void OnDisable() => Desassinar();

        public void DefinirCameraManager(ARCameraManager novo)
        {
            Desassinar();
            cameraManager = novo;
            if (isActiveAndEnabled) Assinar();
        }

        private void Assinar()
        {
            if (assinado || cameraManager == null) return;
            cameraManager.frameReceived += AoReceberFrame;
            assinado = true;
        }

        private void Desassinar()
        {
            if (!assinado) return;
            if (cameraManager != null) cameraManager.frameReceived -= AoReceberFrame;
            assinado = false;
        }

        private unsafe void AoReceberFrame(ARCameraFrameEventArgs args)
        {
            if (OnFrame == null) return; // ninguém usando: nem adquire a imagem
            if (!cameraManager.TryAcquireLatestCpuImage(out XRCpuImage imagem)) return;

            try
            {
                var plano = imagem.GetPlane(0);
                IntPtr pixels = (IntPtr)plano.data.GetUnsafePtr();

                ObterIntrinsecos(imagem.width, imagem.height, out Vector2 foco, out Vector2 centro, out bool reais);

                Transform cam = cameraManager.transform;
                var pose = new Pose(cam.position, cam.rotation);
                Matrix4x4 sensorParaCamera = CalcularSensorParaCamera(imagem.width, imagem.height);
                double timestamp = args.timestampNs.HasValue ? args.timestampNs.Value * 1e-9 : Time.timeAsDouble;

                var frame = new FrameCamera(pixels, imagem.width, imagem.height, plano.rowStride, plano.pixelStride,
                                            foco, centro, reais, pose, sensorParaCamera, timestamp);
                OnFrame?.Invoke(frame);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ArCoreFonteCamera] Erro ao processar frame: {e.Message}");
            }
            finally
            {
                imagem.Dispose(); // o ponteiro do frame deixa de valer aqui
            }
        }

        private void ObterIntrinsecos(int largura, int altura, out Vector2 foco, out Vector2 centro, out bool reais)
        {
            if (cameraManager.TryGetIntrinsics(out XRCameraIntrinsics intr) && intr.resolution.x > 0)
            {
                // Os intrínsecos podem vir numa resolução diferente da imagem da CPU: reescala
                float escala = largura / (float)intr.resolution.x;
                foco = intr.focalLength * escala;
                centro = intr.principalPoint * escala;
                reais = true;
                return;
            }

            // Fallback (ex.: alguns simuladores): estima pelo FOV vertical da câmera de renderização
            if (!avisouSemIntrinsecos)
            {
                Debug.LogWarning("[ArCoreFonteCamera] Intrínsecos indisponíveis; usando estimativa pelo FOV (menos preciso).");
                avisouSemIntrinsecos = true;
            }
            Camera unityCam = cameraManager.GetComponent<Camera>();
            float fov = unityCam != null ? unityCam.fieldOfView : 60f;
            bool retrato = Screen.width < Screen.height;
            float ladoVertical = retrato ? Mathf.Max(largura, altura) : Mathf.Min(largura, altura);
            float f = (ladoVertical * 0.5f) / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            foco = new Vector2(f, f);
            centro = new Vector2(largura * 0.5f, altura * 0.5f);
            reais = false;
        }

        /// <summary>
        /// Para onde vão os eixos do sensor (OpenCV: x direita, y baixo) nos eixos da câmera Unity.
        /// O sensor do celular é fixo no aparelho (deitado); a câmera Unity gira com a tela.
        /// Mantém a mesma regra de orientação que o RastreadorAruco já usava.
        /// </summary>
        private static Matrix4x4 CalcularSensorParaCamera(int largura, int altura)
        {
            bool retrato = Screen.width < Screen.height;
            bool sensorDeitado = largura > altura;

            if (retrato && sensorDeitado)
            {
                return Screen.orientation == ScreenOrientation.PortraitUpsideDown
                    ? Eixos(new Vector3(0, 1, 0), new Vector3(1, 0, 0))     // de cabeça para baixo
                    : Eixos(new Vector3(0, -1, 0), new Vector3(-1, 0, 0));  // retrato normal
            }

            return Screen.orientation == ScreenOrientation.LandscapeRight
                ? Eixos(new Vector3(-1, 0, 0), new Vector3(0, 1, 0))        // paisagem invertida
                : Eixos(new Vector3(1, 0, 0), new Vector3(0, -1, 0));       // paisagem (orientação natural do sensor)
        }

        private static Matrix4x4 Eixos(Vector3 xSensor, Vector3 ySensor)
        {
            Matrix4x4 m = Matrix4x4.identity;
            m.SetColumn(0, xSensor);   // Vector3 -> Vector4 com w = 0
            m.SetColumn(1, ySensor);
            m.SetColumn(2, new Vector4(0, 0, 1, 0));
            return m;
        }
    }
}
