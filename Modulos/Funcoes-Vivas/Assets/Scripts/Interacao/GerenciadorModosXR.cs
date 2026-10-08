using UnityEngine;

public class GerenciadorModosXR : MonoBehaviour
{
    private GeradorDesafioQuadratica gerador;
    private bool modoDesafio = false;

    void Start()
    {
        // Encontra o gerador de desafios na cena
        gerador = FindAnyObjectByType<GeradorDesafioQuadratica>();
        
        // Inicia no modo sandbox (sem bolinhas)
        SetModo(false);
    }

    void SetModo(bool desafio)
    {
        modoDesafio = desafio;
        if (gerador != null)
        {
            gerador.AtivarModo(desafio);
        }
    }

    // Usamos OnGUI desenhado proceduralmente para ser super leve 
    // e não precisar configurar Canvas manualmente no Editor do Unity
    void OnGUI()
    {
        // Escalar GUI para ficar legível em telas mobile (celular em pé ou deitado)
        // Baseamos a escala numa tela padrão de 1080p
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
        
        GUI.color = Color.white; // Reseta cor

        // --- Botão Novo Desafio (Só aparece se estiver no Modo Desafio) ---
        if (modoDesafio && gerador != null)
        {
            GUI.color = new Color(1f, 0.8f, 0f); // Amarelo
            float xNovoDesafio = telaEscaladaWidth - margem * 2 - larguraBotao;
            if (GUI.Button(new Rect(xNovoDesafio, margem * 2, larguraBotao, alturaBotao), "Novo Desafio"))
            {
                gerador.GerarNovoDesafio();
            }
            GUI.color = Color.white;
        }
    }
}
