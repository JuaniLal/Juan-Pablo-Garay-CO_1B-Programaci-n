using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode.Components;

public class GameManager : NetworkBehaviour {
    public static GameManager Instance;

    [Header("Configuración del Loop")]
    public float tiempoDeJuego = 60f;
    public GameObject itemPrefab;
    public float spawnInterval = 3f;
    [Tooltip("Cantidad de esferas que se generarán distribuidas por el mapa al iniciar o reiniciar la partida.")]
    public int esferasIniciales = 30;

    [Header("Límites de la Zona de Spawn Aleatoria (Escala 123)")]
    public float xMinimo = -56f;
    public float xMaximo = 56f;
    public float zMinimo = -56f;
    public float zMaximo = 56f;
    public float alturaY = 0.5f;

    [Header("UI Gameplay")]
    public TextMeshProUGUI txtTiempo;
    public TextMeshProUGUI txtPuntajes;

    [Header("UI Fin de Juego")]
    public GameObject panelFinDeJuego;
    public TextMeshProUGUI txtGanador;
    public Button btnReiniciar;
    public Button btnSalirMenu;

    [Header("UI Menú Principal (Misma Escena)")]
    [Tooltip("Arrastrá acá el PanelMenu interno de tu Canvas de inicio (el que funcionó al activarse).")]
    public GameObject canvasMenuPrincipal;

    private NetworkVariable<float> tiempoRestante = new NetworkVariable<float>(60f);
    private NetworkVariable<bool> juegoActivo = new NetworkVariable<bool>(false);

    public struct JugadorPuntaje : INetworkSerializable, System.IEquatable<JugadorPuntaje> {
        public ulong clientId;
        public int puntos;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter {
            serializer.SerializeValue(ref clientId);
            serializer.SerializeValue(ref puntos);
        }

        public bool Equals(JugadorPuntaje other) {
            return clientId == other.clientId && puntos == other.puntos;
        }
    }

    private NetworkList<JugadorPuntaje> listaPuntajes;
    private float nextSpawnTime;

    private void Awake() {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        listaPuntajes = new NetworkList<JugadorPuntaje>();
    }

    public override void OnNetworkSpawn() {
        listaPuntajes.OnListChanged += OnListaPuntajesCambiara;

        if (IsServer) {
            tiempoRestante.Value = tiempoDeJuego;
            juegoActivo.Value = true;

            NetworkManager.Singleton.OnClientConnectedCallback += OnClienteConectado;
            PosicionarJugador(NetworkManager.Singleton.LocalClientId);

            for (int i = 0; i < esferasIniciales; i++) {
                SpawnItemAleatorio();
            }

            nextSpawnTime = Time.time + 4f;
        }

        panelFinDeJuego.SetActive(false);
        ActualizarUI();
    }

    public override void OnNetworkDespawn() {
        listaPuntajes.OnListChanged -= OnListaPuntajesCambiara;
        if (IsServer && NetworkManager.Singleton != null) {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClienteConectado;
        }
    }

    private void OnClienteConectado(ulong clientId) {
        PosicionarJugador(clientId);

        if (IsServer) {
            bool existe = false;
            foreach (var jp in listaPuntajes) {
                if (jp.clientId == clientId) existe = true;
            }
            if (!existe) {
                listaPuntajes.Add(new JugadorPuntaje { clientId = clientId, puntos = 0 });
            }
        }
    }

    private void OnListaPuntajesCambiara(NetworkListEvent<JugadorPuntaje> changeEvent) {
        ActualizarUI();
    }

    private void PosicionarJugador(ulong clientId) {
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var networkClient)) {
            NetworkObject jugadorNetObj = networkClient.PlayerObject;

            if (jugadorNetObj != null) {
                Vector3 posicionInicial = Vector3.zero;
                Quaternion rotacionInicial = Quaternion.identity;

                switch (clientId) {
                    case 0:
                        posicionInicial = new Vector3(0f, 1f, -35f);
                        rotacionInicial = Quaternion.Euler(0f, 0f, 0f);
                        break;
                    case 1:
                        posicionInicial = new Vector3(0f, 1f, 35f);
                        rotacionInicial = Quaternion.Euler(0f, 180f, 0f);
                        break;
                    case 2:
                        posicionInicial = new Vector3(-35f, 1f, 0f);
                        rotacionInicial = Quaternion.Euler(0f, 90f, 0f);
                        break;
                    case 3:
                        posicionInicial = new Vector3(35f, 1f, 0f);
                        rotacionInicial = Quaternion.Euler(0f, -90f, 0f);
                        break;
                    default:
                        float randomX = Random.Range(xMinimo * 0.5f, xMaximo * 0.5f);
                        float randomZ = Random.Range(zMinimo * 0.5f, zMaximo * 0.5f);
                        posicionInicial = new Vector3(randomX, 1f, randomZ);
                        rotacionInicial = Quaternion.identity;
                        break;
                }

                if (jugadorNetObj.TryGetComponent<PlayerController>(out var controller)) {
                    controller.ResetearEstadoJugador();
                    controller.TeletransportarSeguroClientRpc(posicionInicial, rotacionInicial);
                    controller.CambiarColorCapsulaClientRpc((int)clientId);
                }
                else {
                    jugadorNetObj.transform.position = posicionInicial;
                    jugadorNetObj.transform.rotation = rotacionInicial;
                }
            }
        }
    }

    void Update() {
        if (!juegoActivo.Value) return;

        if (IsServer) {
            tiempoRestante.Value -= Time.deltaTime;
            if (tiempoRestante.Value <= 0) {
                tiempoRestante.Value = 0;
                TerminarPartida();
            }

            if (Time.time >= nextSpawnTime) {
                SpawnItemAleatorio();
                nextSpawnTime = Time.time + spawnInterval;
            }
        }

        if (txtTiempo != null) {
            txtTiempo.text = $"Tiempo: {Mathf.CeilToInt(tiempoRestante.Value)}s";
        }
    }

    [Header("Configuración de Exclusión")]
    public float radioExclusion = 8f;

    void SpawnItemAleatorio() {
        if (itemPrefab == null) return;

        Vector3 posicionAleatoria = Vector3.zero;
        bool posicionValida = false;
        int intentos = 0;
        int maxIntentos = 10;

        do {
            float randomX = Random.Range(xMinimo, xMaximo);
            float randomZ = Random.Range(zMinimo, zMaximo);
            posicionAleatoria = new Vector3(randomX, alturaY, randomZ);

            float distanciaAlCentro = Vector3.Distance(posicionAleatoria, new Vector3(0f, alturaY, 0f));

            if (distanciaAlCentro >= radioExclusion) {
                posicionValida = true;
            }

            intentos++;
        } while (!posicionValida && intentos < maxIntentos);

        GameObject item = Instantiate(itemPrefab, posicionAleatoria, Quaternion.identity);
        item.GetComponent<NetworkObject>().Spawn();
    }

    void ActualizarUI() {
        if (txtPuntajes == null || NetworkManager.Singleton == null) return;

        string textoPuntos = "Puntajes:\n";

        foreach (var jp in listaPuntajes) {
            string codigoColor = "#FFFFFF";

            switch (jp.clientId) {
                case 0: codigoColor = "#FF0000"; break;
                case 1: codigoColor = "#FFFF00"; break;
                case 2: codigoColor = "#3080FF"; break;
                case 3: codigoColor = "#00FF00"; break;
            }

            textoPuntos += $"<color={codigoColor}>Jugador {jp.clientId + 1}: {jp.puntos} pts</color>\n";
        }

        txtPuntajes.text = textoPuntos;
    }

    [ServerRpc(RequireOwnership = false)]
    public void SumarPuntoServerRpc(ulong clientId) {
        if (!juegoActivo.Value) return;

        for (int i = 0; i < listaPuntajes.Count; i++) {
            if (listaPuntajes[i].clientId == clientId) {
                var datosActualizados = listaPuntajes[i];
                datosActualizados.puntos += 1;
                listaPuntajes[i] = datosActualizados;
                break;
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void NotificarGolpeServerRpc(ulong victimaClientId) {
        if (!juegoActivo.Value) return;

        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(victimaClientId, out var networkClient)) {
            if (networkClient.PlayerObject != null && networkClient.PlayerObject.TryGetComponent<PlayerController>(out var controller)) {
                if (controller.VerificarSiTieneObjetoServidor()) {
                    controller.ForzarPerdidaObjetoServidor();
                    SpawnItemAleatorio();
                }
            }
        }
    }

    void TerminarPartida() {
        juegoActivo.Value = false;
        ulong ganadorId = 0;
        int maxPuntos = -1;

        foreach (var jp in listaPuntajes) {
            if (jp.puntos > maxPuntos) {
                maxPuntos = jp.puntos;
                ganadorId = jp.clientId;
            }
        }

        string codigoColor = "#FFFFFF";

        switch (ganadorId) {
            case 0: codigoColor = "#FF0000"; break;
            case 1: codigoColor = "#FFFF00"; break;
            case 2: codigoColor = "#3080FF"; break;
            case 3: codigoColor = "#00FF00"; break;
        }

        // Pinta todo el cartel con el color hexadecimal del jugador ganador
        string mensaje = maxPuntos > -1
            ? $"<color={codigoColor}>Ganador: Jugador {ganadorId + 1} con {maxPuntos} pts</color>"
            : "Empate sin puntos";

        MostrarFinJuegoClientRpc(mensaje);
    }

    // CORRECCIÓN: Agregado el método que Netcode no encontraba para desplegar la UI localmente
    [ClientRpc]
    void MostrarFinJuegoClientRpc(string mensajeGanador) {
        if (txtGanador != null) {
            txtGanador.text = mensajeGanador;
        }

        if (panelFinDeJuego != null) {
            panelFinDeJuego.SetActive(true);
        }

        if (btnReiniciar != null) {
            btnReiniciar.onClick.RemoveAllListeners();
            btnReiniciar.onClick.AddListener(IntentarReiniciarPartida);
        }

        if (btnSalirMenu != null) {
            btnSalirMenu.onClick.RemoveAllListeners();
            btnSalirMenu.onClick.AddListener(SolicitarSalirAlMenu);
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void IntentarReiniciarPartida() {
        if (IsServer) {
            ReiniciarPartida();
        }
        else {
            ReiniciarPartidaServerRpc();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ReiniciarPartidaServerRpc() {
        ReiniciarPartida();
    }

    public void ReiniciarPartida() {
        if (!IsServer) return;

        NetworkObject[] todosLosNetObjects = FindObjectsByType<NetworkObject>(FindObjectsSortMode.None);
        foreach (var netObj in todosLosNetObjects) {
            if (netObj.CompareTag("Item") && netObj.IsSpawned) {
                netObj.Despawn(true);
            }
        }

        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds) {
            PosicionarJugador(clientId);
        }

        while (listaPuntajes.Count > 0) {
            listaPuntajes.RemoveAt(0);
        }

        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds) {
            listaPuntajes.Add(new JugadorPuntaje { clientId = clientId, puntos = 0 });
        }

        for (int i = 0; i < esferasIniciales; i++) {
            SpawnItemAleatorio();
        }

        nextSpawnTime = Time.time + 3f;
        tiempoRestante.Value = tiempoDeJuego;
        juegoActivo.Value = true;

        OcultarFinJuegoClientRpc();
    }

    [ClientRpc]
    void OcultarFinJuegoClientRpc() {
        panelFinDeJuego.SetActive(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void SolicitarSalirAlMenu() {
        if (IsServer) {
            DespacharSalidaGeneral();
        }
        else {
            SolicitarSalirAlMenuServerRpc();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SolicitarSalirAlMenuServerRpc() {
        DespacharSalidaGeneral();
    }

    private void DespacharSalidaGeneral() {
        juegoActivo.Value = false;
        ForzarSalidaMenuClientRpc();
    }

    [ClientRpc]
    private void ForzarSalidaMenuClientRpc() {
        VolverAlMenuLocal();
    }

    private void VolverAlMenuLocal() {
        if (NetworkManager.Singleton != null) {
            NetworkManager.Singleton.Shutdown();
        }

        if (panelFinDeJuego != null) {
            panelFinDeJuego.SetActive(false);
        }

        if (canvasMenuPrincipal != null) {
            canvasMenuPrincipal.SetActive(true);
        }

        Camera camaraEscena = Camera.main;
        if (camaraEscena == null) {
            Camera[] todasLasCamaras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (todasLasCamaras.Length > 0) {
                todasLasCamaras[0].gameObject.SetActive(true);
            }
            else {
                GameObject camRespaldo = new GameObject("Camara_Menu_Respaldo");
                Camera nuevaCam = camRespaldo.AddComponent<Camera>();
                camRespaldo.transform.position = new Vector3(0f, 15f, -25f);
                camRespaldo.transform.rotation = Quaternion.Euler(25f, 0f, 0f);
            }
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}