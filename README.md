# XR-Tangível
Jogos e Quebra-Cabeças em Realidade Mista para o Ensino de Matemática no Ensino Médio.

**XR-Tangível** é um projeto de Iniciação Científica que explora o uso de Realidade Mista (RM) e Interfaces Tangíveis (TUI) para materializar conceitos matemáticos abstratos. Os alunos manipulam peças físicas reais (impressas em 3D e rastreadas por marcadores ArUco) e observam respostas visuais em tempo real, combinando a experiência sensório-motora com visualizações dinâmicas.

## 📦 Módulos do Projeto

O projeto é dividido em 3 módulos principais, alinhados à BNCC e focados nos temas de maior dificuldade no Ensino Médio:

| Módulo | Tema | Dinâmica de Interação |
| :--- | :--- | :--- |
| **FunçõesVivas** | Funções (Linear e Quadrática) | Peças físicas representam coeficientes ($a, b, c$). O gráfico 3D da parábola se deforma e ajusta dinamicamente no espaço real. |
| **SólidosMix** | Geometria Espacial | Polígonos físicos (2D) são encaixados, e o aplicativo projeta sólidos geométricos completos em RM a partir de suas faces. |
| **TrigoWorld** | Trigonometria | Manipulação de um transferidor físico instrumentalizado para construir triângulos retângulos e explorar o círculo trigonométrico. |

> *Nota: Atualmente, apenas o módulo **FunçõesVivas** está implementado no repositório.*

## 🛠️ Stack Tecnológica

O projeto foi desenvolvido para rodar de forma leve e responsiva em dispositivos móveis (Android) e óculos de RM:
- **Engine**: Unity 3D (LTS)
- **Rastreamento Físico**: OpenCV via DLL customizada em C++ para marcadores ArUco.
- **Rastreamento de Mãos**: Unity Sentis + Google MediaPipe (BlazeHand) para inference de rede neural em tempo real no celular.
- **Licença**: Código aberto (MIT)

## 📁 Estrutura de Pastas

```
XR-Tangível/
├── Papers/               # Referências bibliográficas e artigos de base
├── projeto_ic_xr_tangivel.pdf  # Proposta original da Iniciação Científica
└── Modulos/
    └── Funcoes-Vivas/    # Projeto Unity Principal
        └── Assets/
            ├── Scripts/
            │   ├── Rastreamento/ # Tracking de Mãos (Sentis) e Marcadores ArUco
            │   ├── Visualizacao/ # Desenho vetorial da parábola e gráficos
            │   ├── Interacao/    # Controle de input, toque na tela, pinça
            │   └── Desafios/     # Lógica dos quebra-cabeças educacionais
            ├── Plugins/      # DLL nativa de OpenCV e ArUco (C++) para Android/Windows
            └── Resources/    # Modelos ONNX (IA) e dados globais (âncoras)
```

## 🚀 Como Abrir e Executar no Unity

1. Abra o Unity Hub.
2. Clique em **Add / Open** e selecione a pasta `XR-Tangivel/Modulos/Funcoes-Vivas`.
3. Garanta que você possui os módulos **Android Build Support** instalados no seu Unity Editor.
4. Abra a cena principal em `Assets/Scenes/SampleScene.unity`.
5. Para testar a visualização, dê **Play** no Editor. Para usar o rastreamento, compile para seu aparelho Android (`File -> Build And Run`).
