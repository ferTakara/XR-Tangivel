using UnityEngine;

namespace XRTangivel.Core
{
    /// <summary>
    /// Interface para fornecer pose de ancoragem no mundo real (posição e rotação),
    /// seja pelo ArUco 40 (Mobile), Spatial Anchors (Meta Quest) ou Plane Detection.
    /// </summary>
    public interface IAncoraMundoProvider
    {
        bool EstaAncorado { get; }
        bool TentarObterPose(out Vector3 posicao, out Quaternion rotacao);
        void Desancorar();
    }
}
