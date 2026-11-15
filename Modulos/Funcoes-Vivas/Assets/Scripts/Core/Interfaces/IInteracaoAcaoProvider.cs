using System;

namespace XRTangivel.Core
{
    /// <summary>
    /// Interface para ações e eventos de interação do usuário,
    /// como alternar modo (Sandbox/Desafio), pedir novo desafio ou confirmar resposta.
    /// Pode ser disparado por botões na tela (Mobile), botões de papel (ArUco Occlusion)
    /// ou botões 3D / gestos de mão (Meta Quest Poke/Pinch).
    /// </summary>
    public interface IInteracaoAcaoProvider
    {
        event Action OnAlternarModo;
        event Action OnNovoDesafio;
        event Action OnResetarPosicao;
    }
}
