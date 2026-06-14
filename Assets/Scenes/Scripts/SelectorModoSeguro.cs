using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode; // NUEVO: Necesario para detectar el Shutdown de Netcode

public class SelectorModoSeguro : MonoBehaviour {
    [Header("Botones del Menú Principal")]
    public Button btnCrearHost;
    public Button btnUnirseCliente;

    [Header("Configuración del Puerto de Escucha")]
    [Tooltip("Puerto auxiliar libre para el latido de presencia.")]
    public int puertoLatido = 7785;

    private UdpClient udpReceptor;
    private Thread hiloEscucha;
    private bool escuchando = false;
    private bool hostDetectado = false;

    private DateTime horaUltimoLatido;
    private bool soyHost = false;

    void Start() {
        // Iniciamos el sistema por primera vez
        InicializarTodo();
    }

    void OnEnable() {
        // Cada vez que el panel del menú se vuelve a prender en pantalla,
        // nos suscribimos al evento de Netcode para saber si nos desconectamos.
        if (NetworkManager.Singleton != null) {
            NetworkManager.Singleton.OnClientStopped += AlDetenerseLaRed;
        }
    }

    void OnDisable() {
        if (NetworkManager.Singleton != null) {
            NetworkManager.Singleton.OnClientStopped -= AlDetenerseLaRed;
        }
    }

    void Update() {
        if (soyHost) return;

        if (hostDetectado) {
            TimeSpan tiempoTranscurrido = DateTime.Now - horaUltimoLatido;

            if (tiempoTranscurrido.TotalSeconds > 3f) {
                hostDetectado = false;
                if (btnCrearHost != null) btnCrearHost.interactable = true;
            }
            else {
                if (btnCrearHost != null && btnCrearHost.interactable) {
                    btnCrearHost.interactable = false;
                }
            }
        }
    }

    // Vinculado al OnClick() de tu botón de Crear Host
    public void RegistrarInicioHost() {
        if (soyHost) return;

        soyHost = true;
        hostDetectado = false;

        if (btnCrearHost != null) btnCrearHost.interactable = false;

        ApagarEscucha();

        // El Host empieza a emitir su presencia hacia la red local
        InvokeRepeating(nameof(EmitirLatidoPresencia), 0.1f, 0.8f);
    }

    private void EmitirLatidoPresencia() {
        try {
            using (UdpClient emisorTemporal = new UdpClient()) {
                emisorTemporal.EnableBroadcast = true;
                byte[] datos = Encoding.UTF8.GetBytes("HOST_ALIVE");
                IPEndPoint endPoint = new IPEndPoint(IPAddress.Broadcast, puertoLatido);
                emisorTemporal.Send(datos, datos.Length, endPoint);
            }
        }
        catch (Exception e) {
            Debug.LogWarning("Fallo al emitir latido de Host: " + e.Message);
        }
    }

    private void InicializarTodo() {
        // Forzamos el estado inicial limpio
        soyHost = false;
        hostDetectado = false;
        if (btnCrearHost != null) btnCrearHost.interactable = true;
        if (btnUnirseCliente != null) btnUnirseCliente.interactable = true;

        IniciarEscuchaCompartida();
    }

    private void IniciarEscuchaCompartida() {
        try {
            udpReceptor = new UdpClient();
            udpReceptor.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            IPEndPoint localEP = new IPEndPoint(IPAddress.Any, puertoLatido);
            udpReceptor.Client.Bind(localEP);

            escuchando = true;
            hiloEscucha = new Thread(EscucharRedLocal);
            hiloEscucha.IsBackground = true;
            hiloEscucha.Start();
        }
        catch (Exception e) {
            Debug.LogError("No se pudo inicializar el receptor del menú: " + e.Message);
        }
    }

    private void EscucharRedLocal() {
        IPEndPoint grupoIp = new IPEndPoint(IPAddress.Any, puertoLatido);
        while (escuchando) {
            try {
                if (udpReceptor == null || udpReceptor.Client == null) break;

                byte[] bytesRecibidos = udpReceptor.Receive(ref grupoIp);
                string mensaje = Encoding.UTF8.GetString(bytesRecibidos);

                if (mensaje == "HOST_ALIVE") {
                    hostDetectado = true;
                    horaUltimoLatido = DateTime.Now;
                }
            }
            catch (SocketException) {
                break;
            }
            catch {
                break;
            }
        }
    }

    // NUEVO: Método clave que gatilla Netcode al hacer el Shutdown masivo o individual
    private void AlDetenerseLaRed(bool esServer) {
        // Frenamos los loops de envíos de datos de inmediato
        CancelInvoke();
        ApagarEscucha();

        // Limpiamos y re-armamos los sockets desde cero para la próxima partida
        InicializarTodo();
    }

    private void ApagarEscucha() {
        escuchando = false;
        if (udpReceptor != null) {
            udpReceptor.Close();
            udpReceptor = null;
        }
        if (hiloEscucha != null && hiloEscucha.IsAlive) {
            hiloEscucha.Join(30);
        }
    }

    private void OnDestroy() {
        CancelInvoke();
        ApagarEscucha();
    }
}