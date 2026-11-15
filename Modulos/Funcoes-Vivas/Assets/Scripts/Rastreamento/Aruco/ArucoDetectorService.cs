using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using XRTangivel.Core;

namespace XRTangivel.Rastreamento
{
    /// <summary>Um marcador ArUco encontrado num frame.</summary>
    public struct MarcadorDetectado
    {
        public int id;
        /// <summary>Cantos em pixels da imagem do sensor: sup-esq, sup-dir, inf-dir, inf-esq.</summary>
        public Vector2 c0, c1, c2, c3;

        /// <summary>true se a pose 3D foi calculada (marcador grande o bastante e geometria válida).</summary>
        public bool temPose;
        public Vector3 posicaoMundo;
        public Vector3 eixoXMundo, eixoYMundo, normalMundo;
        /// <summary>Distância da câmera ao centro do marcador (m).</summary>
        public float distanciaMetros;

        /// <summary>Ângulo da aresta superior na imagem (usado pelos dials A/B/C).</summary>
        public float AnguloImagemGraus => Mathf.Atan2(c1.y - c0.y, c1.x - c0.x) * Mathf.Rad2Deg;

        public float LadoMedioPx =>
            (Vector2.Distance(c0, c1) + Vector2.Distance(c1, c2) + Vector2.Distance(c2, c3) + Vector2.Distance(c3, c0)) * 0.25f;
    }

    /// <summary>
    /// Detecta marcadores ArUco num FrameCamera (DLL nativa OpenCV) e calcula a pose 3D de cada um
    /// com os intrínsecos e a pose da câmera do frame. Não depende de ARCore: serve para o celular
    /// e para o Quest, desde que a fonte de câmera preencha o FrameCamera.
    /// </summary>
    public class ArucoDetectorService
    {
        [DllImport("MinhaArucoDLL2")]
        private static extern int DetectarMultiplosArucos(
            IntPtr pixels, int width, int height, int channels,
            int maxMarcadores, int[] outIds, float[] outCorners);

        public const int MAX_MARCADORES = 10;

        /// <summary>Lado do marcador impresso (m). Todos os marcadores usam o mesmo tamanho.</summary>
        public float tamanhoMarcadorMetros = 0.08f;
        /// <summary>Abaixo deste lado (px) a pose não é calculada: o marcador está pequeno/longe demais.</summary>
        public float ladoMinimoPx = 8f;
        /// <summary>false = só cantos e IDs (pula a pose 3D por homografia).</summary>
        public bool calcularPose = true;

        private readonly int[] bufferIds = new int[MAX_MARCADORES];
        private readonly float[] bufferCantos = new float[MAX_MARCADORES * 8];
        private readonly List<MarcadorDetectado> resultado = new List<MarcadorDetectado>(MAX_MARCADORES);
        // Usado quando as linhas têm "padding" (rowStride > largura): a DLL assume linhas contíguas
        private byte[] bufferCompacto;

        /// <summary>
        /// Detecta os marcadores do frame. A lista devolvida é reaproveitada no próximo frame:
        /// copie o que precisar guardar.
        /// </summary>
        public IReadOnlyList<MarcadorDetectado> Detectar(in FrameCamera frame)
        {
            resultado.Clear();
            int total = ChamarDll(frame);

            for (int i = 0; i < total; i++)
            {
                int o = i * 8;
                var m = new MarcadorDetectado
                {
                    id = bufferIds[i],
                    c0 = new Vector2(bufferCantos[o + 0], bufferCantos[o + 1]),
                    c1 = new Vector2(bufferCantos[o + 2], bufferCantos[o + 3]),
                    c2 = new Vector2(bufferCantos[o + 4], bufferCantos[o + 5]),
                    c3 = new Vector2(bufferCantos[o + 6], bufferCantos[o + 7]),
                };

                if (calcularPose && m.LadoMedioPx >= ladoMinimoPx &&
                    PoseMarcadorPlanar.Estimar(m.c0, m.c1, m.c2, m.c3, frame.focoPx, frame.centroPx, tamanhoMarcadorMetros,
                                               out Vector3 centro, out Vector3 eixoX, out Vector3 eixoY, out Vector3 normal))
                {
                    m.temPose = true;
                    m.distanciaMetros = centro.magnitude;
                    m.posicaoMundo = frame.SensorParaMundoPonto(centro);
                    m.eixoXMundo = frame.SensorParaMundoDirecao(eixoX);
                    m.eixoYMundo = frame.SensorParaMundoDirecao(eixoY);
                    m.normalMundo = frame.SensorParaMundoDirecao(normal);
                }

                resultado.Add(m);
            }

            return resultado;
        }

        private unsafe int ChamarDll(in FrameCamera frame)
        {
            int bytesPorLinha = frame.largura * frame.bytesPorPixel;

            if (frame.rowStride == bytesPorLinha)
            {
                return DetectarMultiplosArucos(frame.pixels, frame.largura, frame.altura, frame.bytesPorPixel,
                                               MAX_MARCADORES, bufferIds, bufferCantos);
            }

            // Linhas com padding (comum no Android): copia linha a linha para um buffer contíguo
            int tamanho = bytesPorLinha * frame.altura;
            if (bufferCompacto == null || bufferCompacto.Length < tamanho)
                bufferCompacto = new byte[tamanho];

            byte* origem = (byte*)frame.pixels;
            fixed (byte* destino = bufferCompacto)
            {
                for (int linha = 0; linha < frame.altura; linha++)
                    UnsafeUtility.MemCpy(destino + linha * bytesPorLinha, origem + linha * frame.rowStride, bytesPorLinha);

                return DetectarMultiplosArucos((IntPtr)destino, frame.largura, frame.altura, frame.bytesPorPixel,
                                               MAX_MARCADORES, bufferIds, bufferCantos);
            }
        }
    }
}
