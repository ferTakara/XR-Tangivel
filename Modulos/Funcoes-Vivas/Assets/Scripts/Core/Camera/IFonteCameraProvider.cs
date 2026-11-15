using System;
using UnityEngine;

namespace XRTangivel.Core
{
    /// <summary>
    /// Um frame de câmera para visão computacional. ATENÇÃO: o ponteiro de pixels só é válido
    /// durante o callback OnFrame (a memória é liberada logo depois).
    /// </summary>
    public readonly struct FrameCamera
    {
        /// <summary>Primeiro plano da imagem (tons de cinza no celular; RGBA no XR Simulation).</summary>
        public readonly IntPtr pixels;
        public readonly int largura, altura;
        /// <summary>Bytes entre o início de uma linha e o da próxima (pode ser maior que largura * bytesPorPixel).</summary>
        public readonly int rowStride;
        public readonly int bytesPorPixel;

        /// <summary>Distância focal e ponto principal em pixels DESTA imagem.</summary>
        public readonly Vector2 focoPx, centroPx;
        /// <summary>false = foco estimado pelo FOV da câmera (menos preciso).</summary>
        public readonly bool intrinsecosReais;

        /// <summary>Pose da câmera no mundo Unity no instante do frame.</summary>
        public readonly Pose poseCameraMundo;
        /// <summary>
        /// Converte direções do sensor (convenção OpenCV: x direita, y para baixo, z para frente)
        /// para os eixos locais da câmera Unity (x direita, y para cima, z para frente).
        /// Depende da orientação da tela no celular.
        /// </summary>
        public readonly Matrix4x4 sensorParaCamera;
        public readonly double timestampSegundos;

        public FrameCamera(IntPtr pixels, int largura, int altura, int rowStride, int bytesPorPixel,
                           Vector2 focoPx, Vector2 centroPx, bool intrinsecosReais,
                           Pose poseCameraMundo, Matrix4x4 sensorParaCamera, double timestampSegundos)
        {
            this.pixels = pixels;
            this.largura = largura;
            this.altura = altura;
            this.rowStride = rowStride;
            this.bytesPorPixel = bytesPorPixel;
            this.focoPx = focoPx;
            this.centroPx = centroPx;
            this.intrinsecosReais = intrinsecosReais;
            this.poseCameraMundo = poseCameraMundo;
            this.sensorParaCamera = sensorParaCamera;
            this.timestampSegundos = timestampSegundos;
        }

        /// <summary>Converte um ponto/direção do sensor (OpenCV) para o mundo Unity.</summary>
        public Vector3 SensorParaMundoPonto(Vector3 pontoSensor)
            => poseCameraMundo.position + poseCameraMundo.rotation * sensorParaCamera.MultiplyVector(pontoSensor);

        public Vector3 SensorParaMundoDirecao(Vector3 direcaoSensor)
            => poseCameraMundo.rotation * sensorParaCamera.MultiplyVector(direcaoSensor);
    }

    /// <summary>
    /// Fonte de imagem da câmera, independente de plataforma:
    /// ARCore no celular (ArCoreFonteCamera) ou Passthrough Camera API no Quest 3/3S (QuestFonteCamera).
    /// </summary>
    public interface IFonteCameraProvider
    {
        event Action<FrameCamera> OnFrame;
        bool EstaAtiva { get; }
    }
}
