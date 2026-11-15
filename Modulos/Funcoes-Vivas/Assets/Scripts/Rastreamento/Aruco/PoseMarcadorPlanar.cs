using UnityEngine;

namespace XRTangivel.Rastreamento
{
    /// <summary>
    /// Pose 3D de um marcador quadrado a partir dos 4 cantos na imagem e dos intrínsecos da câmera,
    /// via homografia planar (equivalente a um solvePnP simples, sem OpenCV no C#).
    /// Resultado na convenção do sensor/OpenCV: x direita, y para baixo, z para frente.
    /// Validado numericamente: exato sem ruído; com 0,3 px de ruído nos cantos, erro mediano de posição ~0,5%.
    /// </summary>
    public static class PoseMarcadorPlanar
    {
        /// <param name="cantosPx">4 cantos na ordem do ArUco: sup-esq, sup-dir, inf-dir, inf-esq.</param>
        /// <param name="ladoMetros">Lado do marcador impresso, em metros.</param>
        /// <param name="centro">Posição do centro do marcador (m), eixos do sensor.</param>
        /// <param name="eixoX">Eixo X do marcador (do canto 0 para o canto 1), eixos do sensor.</param>
        /// <param name="eixoY">Eixo Y do marcador (do canto 3 para o canto 0), eixos do sensor.</param>
        /// <param name="normal">Normal do marcador (aponta para fora do papel), eixos do sensor.</param>
        public static bool Estimar(Vector2 c0, Vector2 c1, Vector2 c2, Vector2 c3,
                                   Vector2 focoPx, Vector2 centroPx, float ladoMetros,
                                   out Vector3 centro, out Vector3 eixoX, out Vector3 eixoY, out Vector3 normal)
        {
            centro = eixoX = eixoY = normal = Vector3.zero;
            if (focoPx.x <= 0f || focoPx.y <= 0f || ladoMetros <= 0f) return false;

            double h = ladoMetros * 0.5;
            // Pontos no plano do marcador (mesma convenção do estimatePoseSingleMarkers do OpenCV)
            double[] X = { -h, h, h, -h };
            double[] Y = { h, h, -h, -h };
            // Cantos em coordenadas normalizadas (K^-1 aplicado)
            double[] u = { N(c0.x, centroPx.x, focoPx.x), N(c1.x, centroPx.x, focoPx.x), N(c2.x, centroPx.x, focoPx.x), N(c3.x, centroPx.x, focoPx.x) };
            double[] v = { N(c0.y, centroPx.y, focoPx.y), N(c1.y, centroPx.y, focoPx.y), N(c2.y, centroPx.y, focoPx.y), N(c3.y, centroPx.y, focoPx.y) };

            // Sistema 8x8 para a homografia (h33 = 1)
            var A = new double[8, 9];
            for (int i = 0; i < 4; i++)
            {
                int r = 2 * i;
                A[r, 0] = X[i]; A[r, 1] = Y[i]; A[r, 2] = 1; A[r, 6] = -u[i] * X[i]; A[r, 7] = -u[i] * Y[i]; A[r, 8] = u[i];
                A[r + 1, 3] = X[i]; A[r + 1, 4] = Y[i]; A[r + 1, 5] = 1; A[r + 1, 6] = -v[i] * X[i]; A[r + 1, 7] = -v[i] * Y[i]; A[r + 1, 8] = v[i];
            }
            if (!Resolver(A, out double[] hv)) return false;

            // H = [h1 h2 h3] = lambda * [r1 r2 t]
            double h1x = hv[0], h1y = hv[3], h1z = hv[6];
            double h2x = hv[1], h2y = hv[4], h2z = hv[7];
            double h3x = hv[2], h3y = hv[5], h3z = 1.0;

            double n1 = System.Math.Sqrt(h1x * h1x + h1y * h1y + h1z * h1z);
            double n2 = System.Math.Sqrt(h2x * h2x + h2y * h2y + h2z * h2z);
            if (n1 < 1e-12 || n2 < 1e-12) return false;
            double lambda = 2.0 / (n1 + n2);
            if (h3z * lambda < 0) lambda = -lambda; // o marcador tem que estar na frente da câmera

            var r1 = new Vector3((float)(h1x * lambda), (float)(h1y * lambda), (float)(h1z * lambda));
            var r2 = new Vector3((float)(h2x * lambda), (float)(h2y * lambda), (float)(h2z * lambda));
            var t = new Vector3((float)(h3x * lambda), (float)(h3y * lambda), (float)(h3z * lambda));

            // Ortonormaliza (Gram-Schmidt): o ruído deixa r1 e r2 um pouco tortos
            r1.Normalize();
            r2 = (r2 - r1 * Vector3.Dot(r1, r2)).normalized;
            // Produto vetorial na convenção destra do sensor (não usar Vector3.Cross pensando em Unity aqui:
            // a fórmula é a mesma, só a interpretação dos eixos muda)
            Vector3 r3 = Vector3.Cross(r1, r2);

            if (t.z <= 0.01f) return false;

            centro = t;
            eixoX = r1;
            eixoY = r2;
            normal = r3;
            return true;
        }

        private static double N(float px, float c, float f) => (px - c) / f;

        /// <summary>Eliminação de Gauss-Jordan com pivoteamento parcial (matriz aumentada 8x9).</summary>
        private static bool Resolver(double[,] M, out double[] x)
        {
            const int n = 8;
            x = new double[n];
            for (int c = 0; c < n; c++)
            {
                int p = c;
                double maior = System.Math.Abs(M[c, c]);
                for (int r = c + 1; r < n; r++)
                {
                    double val = System.Math.Abs(M[r, c]);
                    if (val > maior) { maior = val; p = r; }
                }
                if (maior < 1e-12) return false; // cantos degenerados (colineares)

                if (p != c)
                    for (int k = 0; k <= n; k++) { double tmp = M[c, k]; M[c, k] = M[p, k]; M[p, k] = tmp; }

                for (int r = 0; r < n; r++)
                {
                    if (r == c) continue;
                    double fator = M[r, c] / M[c, c];
                    if (fator == 0) continue;
                    for (int k = c; k <= n; k++) M[r, k] -= fator * M[c, k];
                }
            }
            for (int i = 0; i < n; i++) x[i] = M[i, n] / M[i, i];
            return true;
        }
    }
}
