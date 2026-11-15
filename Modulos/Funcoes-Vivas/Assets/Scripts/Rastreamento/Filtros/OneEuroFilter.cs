using UnityEngine;

namespace XRTangivel.Rastreamento
{
    /// <summary>
    /// One Euro Filter (Casiez et al., CHI 2012): filtra forte quando o sinal está parado (mata o tremor)
    /// e solta quando ele se move (pouco atraso). Extraído do RastreadorAruco para ser reaproveitado
    /// (ex.: rastreamento de mão).
    /// </summary>
    public class OneEuroFilterFloat
    {
        private readonly float minCutoff, beta, dCutoff;
        private float prevValue, prevDeriv, prevTime;
        private bool started;

        public OneEuroFilterFloat(float minCutoff = 0.3f, float beta = 0.00001f, float dCutoff = 1.0f)
        {
            this.minCutoff = minCutoff;
            this.beta = beta;
            this.dCutoff = dCutoff;
        }

        private static float Alpha(float cutoff, float dt)
        {
            float tau = 1.0f / (2f * Mathf.PI * cutoff);
            return 1.0f / (1.0f + tau / Mathf.Max(dt, 0.001f));
        }

        public float Filter(float value, float timestamp)
        {
            if (!started)
            {
                prevValue = value;
                prevDeriv = 0f;
                prevTime = timestamp;
                started = true;
                return value;
            }

            float dt = timestamp - prevTime;
            if (dt <= 0f) dt = 1f / 30f;
            prevTime = timestamp;

            float deriv = (value - prevValue) / dt;
            float aD = Alpha(dCutoff, dt);
            deriv = aD * deriv + (1f - aD) * prevDeriv;

            float cutoff = minCutoff + beta * Mathf.Abs(deriv);
            float a = Alpha(cutoff, dt);
            float filtered = a * value + (1f - a) * prevValue;

            prevValue = filtered;
            prevDeriv = deriv;
            return filtered;
        }

        public void Reset() { started = false; }
    }

    public class OneEuroFilterVec3
    {
        private readonly OneEuroFilterFloat fx, fy, fz;

        public OneEuroFilterVec3(float minCutoff = 0.3f, float beta = 0.00001f, float dCutoff = 1.0f)
        {
            fx = new OneEuroFilterFloat(minCutoff, beta, dCutoff);
            fy = new OneEuroFilterFloat(minCutoff, beta, dCutoff);
            fz = new OneEuroFilterFloat(minCutoff, beta, dCutoff);
        }

        public Vector3 Filter(Vector3 v, float t)
        {
            return new Vector3(fx.Filter(v.x, t), fy.Filter(v.y, t), fz.Filter(v.z, t));
        }

        public void Reset() { fx.Reset(); fy.Reset(); fz.Reset(); }
    }
}
