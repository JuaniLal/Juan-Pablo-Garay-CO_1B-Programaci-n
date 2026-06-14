using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;

public class NetworkMenuUI : MonoBehaviour {
    public InputField inputIP;
    public Button btnHost;
    public Button btnCliente;
    public GameObject panelMenu;
    public Text txtAutor;

    void Start() {
        
        txtAutor.text = "Desarrollado por: Juan Pablo Garay.";
        inputIP.text = "127.0.0.1"; // IP por defecto (Localhost)

        btnHost.onClick.AddListener(IniciarHost);
        btnCliente.onClick.AddListener(IniciarCliente);
    }

    void IniciarHost() {
        ConfigurarTransporte();
        NetworkManager.Singleton.StartHost();
        panelMenu.SetActive(false);
    }

    void IniciarCliente() {
        ConfigurarTransporte();
        NetworkManager.Singleton.StartClient();
        panelMenu.SetActive(false);
    }

    void ConfigurarTransporte() {
        
        var transporte = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transporte != null && !string.IsNullOrEmpty(inputIP.text)) {
            transporte.ConnectionData.Address = inputIP.text;
        }
    }
}