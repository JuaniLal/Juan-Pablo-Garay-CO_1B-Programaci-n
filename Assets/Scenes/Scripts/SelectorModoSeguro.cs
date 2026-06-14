using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

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
        // Iniciamos la escucha de fondo de forma compartida y segura
        IniciarEscuchaCompartida();
    }

    void Update() {
        // El Host no necesita auto-grisarse por timout
        if (soyHost) return;

        if (hostDetectado) {
            TimeSpan tiempoTranscurrido = DateTime.Now - horaUltimoLatido;

            if (tiempoTranscurrido.TotalSeconds > 3f) {
                // Si el host desaparece por más de 3 segundos, liberamos el botón
                hostDetectado = false;
                if (btnCrearHost != null) btnCrearHost.interactable = true;
            }
            else {
                // Forzar el grisado inmediato si hay un host vivo detectado
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

        // El host apaga su receptor local para no auto-escucharse
        ApagarEscucha();

        // El Host empieza a emitir su presencia hacia la red local de forma indefinida
        InvokeRepeating(nameof(EmitirLatidoPresencia), 0.1f, 0.8f);
    }

    private void EmitirLatidoPresencia() {
        // Creamos un emisor temporal con puerto efímero (aleatorio asignado por Windows)
        // para evitar CUALQUIER colisión de sockets con los clientes
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

    private void IniciarEscuchaCompartida() {
        try {
            // CONFIGURACIÓN AVANZADA DE SOCKET: Forzamos a Windows a permitir la reutilización de este puerto
            // Esto permite que el Editor 1, el Editor 2 y las Builds escuchen el puerto 7785 al mismo tiempo en la misma PC.
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
                    // ¡Encontramos al Host! Marcamos el tiempo actual
                    hostDetectado = true;
                    horaUltimoLatido = DateTime.Now;
                }
            }
            catch (SocketException) {
                // Salida limpia cuando cerramos el socket manualmente
                break;
            }
            catch {
                break;
            }
        }
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
        ApagarEscucha();
        CancelInvoke();
    }
}