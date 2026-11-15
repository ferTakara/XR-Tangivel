using UnityEngine;
using XRTangivel.Core;

/// <summary>
/// Interface de tela do celular (OnGUI): só mostra o estado e repassa os cliques ao ControladorSessao,
/// que é o dono do modo e dos desafios.
/// </summary>
public class GerenciadorModosXR : MonoBehaviour
{
    private ControladorSessao sessao;
    private ControladorSessao Sessao => sessao != null ? sessao : (sessao = ControladorSessao.Obter());

    void Start()
    {
        sessao = ControladorSessao.Obter();
    }

    public void AlternarModo() => Sessao.AlternarModo();
    public void SetModo(bool desafio) => Sessao.SetModo(desafio);

    // OnGUI desenhado por código para ser leve e não precisar configurar Canvas no Editor
    void OnGUI()
    {
        bool modoDesafio = Sessao.ModoDesafio;

        // Escala a GUI para ficar legível no celular (em pé ou deitado), com base numa tela de 1080p
        float escala = Screen.width > Screen.height ? Screen.height / 1080f : Screen.width / 1080f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(escala, escala, 1f));

        int larguraBotao = 260;
        int alturaBotao = 110;
        int margem = 20;

        GUI.skin.button.fontSize = 35;
        GUI.skin.box.fontSize = 35;

        // Fundo semitransparente no topo
        float telaEscaladaWidth = Screen.width / escala;
        GUI.Box(new Rect(margem, margem, telaEscaladaWidth - margem * 2, alturaBotao + margem * 2), "");

        // --- Botão Modo Livre (Sandbox) ---
        GUI.color = !modoDesafio ? Color.green : Color.white;
        if (GUI.Button(new Rect(margem * 2, margem * 2, larguraBotao, alturaBotao), "Livre (Sandbox)"))
        {
            SetModo(false);
        }

        // --- Botão Modo Desafio ---
        GUI.color = modoDesafio ? Color.green : Color.white;
        if (GUI.Button(new Rect(margem * 3 + larguraBotao, margem * 2, larguraBotao, alturaBotao), "Desafios"))
        {
            SetModo(true);
        }

        GUI.color = Color.white;

        // --- Botão Novo Desafio (só no modo desafio) ---
        if (modoDesafio && Sessao.TemGerador)
        {
            GUI.color = new Color(1f, 0.8f, 0f); // amarelo
            float xNovoDesafio = telaEscaladaWidth - margem * 2 - larguraBotao;
            if (GUI.Button(new Rect(xNovoDesafio, margem * 2, larguraBotao, alturaBotao), "Novo Desafio"))
            {
                Sessao.PedirNovoDesafio();
            }
            GUI.color = Color.white;
        }
    }
}
