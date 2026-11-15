using UnityEngine;

public class AcaoTesteConsole : MonoBehaviour
{
    public void AvisarCliqueConsole()
    {
        // 1. Mostra no Console do Unity e no Android Logcat
        Debug.Log("🔥 SUCESSO: O evento do botão físico ArUco acionou este script perfeitamente! 🔥");
        
        // 2. Faz o celular vibrar rapidamente (feedback tátil)
        Handheld.Vibrate();
    }
}
