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

    [Header("Configuración de Música (50/50)")]
    [Tooltip("Arrastrá acá tu primer pista de música hecha por vos.")]
    public AudioClip pistaMúsica1;
    [Tooltip("Arrastrá acá tu segunda pista de música hecha por vos.")]
    public AudioClip pistaMúsica2;

    private AudioSource reproductorMúsica;

    private NetworkVariable<float> tiempoRestante = new NetworkVariable<float>(60f);

    public NetworkVariable<bool> juegoActivo = new NetworkVariable<bool>(false);
    private bool juegoActivoLocal = false;

    public bool JuegoActivo => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening ? juegoActivo.Value : juegoActivoLocal;

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
        reproductorMúsica = GetComponent<AudioSource>();
    }

    public override void OnNetworkSpawn() {
        listaPuntajes.OnListChanged += OnListaPuntajesCambiara;
        juegoActivoLocal = true;

        if (IsServer) {
            if (listaPuntajes != null) {
                while (listaPuntajes.Count > 0) {
                    listaPuntajes.RemoveAt(0);
                }
            }

            tiempoRestante.Value = tiempoDeJuego;
            juegoActivo.Value = true;

            NetworkManager.Singleton.OnClientConnectedCallback += OnClienteConectado;

            listaPuntajes.Add(new JugadorPuntaje { clientId = NetworkManager.Singleton.LocalClientId, puntos = 0 });
            StartCoroutine(Co_PosicionarConRetraso(NetworkManager.Singleton.LocalClientId, 0));

            for (int i = 0; i < esferasIniciales; i++) {
                SpawnItemAleatorio();
            }

            nextSpawnTime = Time.time + 4f;

            // El Host decide la pista inicial y arranca a tocar localmente
            ElegirYReproducirMúsicaLocal();
        }

        panelFinDeJuego.SetActive(false);
        ActualizarUI();
    }

    private void ElegirYReproducirMúsicaLocal() {
        if (reproductorMúsica == null || pistaMúsica1 == null || pistaMúsica2 == null) return;

        reproductorMúsica.Stop();

        if (Random.value < 0.5f) {
            reproductorMúsica.clip = pistaMúsica1;
        }
        else {
            reproductorMúsica.clip = pistaMúsica2;
        }

        reproductorMúsica.Play();
    }

    public override void OnNetworkDespawn() {
        juegoActivoLocal = false;
        listaPuntajes.OnListChanged -= OnListaPuntajesCambiara;
        if (IsServer && NetworkManager.Singleton != null) {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClienteConectado;
        }
    }

    private void OnClienteConectado(ulong clientId) {
        if (!IsServer) return;

        bool existe = false;
        for (int i = 0; i < listaPuntajes.Count; i++) {
            if (listaPuntajes[i].clientId == clientId) existe = true;
        }

        if (!existe) {
            int indiceRealOcupado = listaPuntajes.Count;
            listaPuntajes.Add(new JugadorPuntaje { clientId = clientId, puntos = 0 });

            StartCoroutine(Co_PosicionarConRetraso(clientId, indiceRealOcupado));

            // MODIFICACIÓN: Si la música está sonando en el Host, le enviamos la pista exacta y el segundo actual al cliente que entra
            if (reproductorMúsica != null && reproductorMúsica.isPlaying) {
                int indicePista = (reproductorMúsica.clip == pistaMúsica1) ? 1 : 2;
                float tiempoActualPista = reproductorMúsica.time;

                ClientRpcParams parametrosClienteEspecífico = new ClientRpcParams {
                    Send = new ClientRpcSendParams {
                        TargetClientIds = new List<ulong> { clientId }
                    }
                };

                SincronizarMúsicaClienteNuevoClientRpc(indicePista, tiempoActualPista, parametrosClienteEspecífico);
            }
        }
    }

    // NUEVO RPC: Sincroniza la música de forma tardía únicamente para el cliente que se conecta a mitad de partida
    [ClientRpc]
    private void SincronizarMúsicaClienteNuevoClientRpc(int indicePista, float tiempoInicio, ClientRpcParams clientRpcParams = default) {
        // El Host no necesita auto-sincronizarse porque ya inició la reproducción de forma nativa
        if (IsServer) return;

        if (reproductorMúsica == null || pistaMúsica1 == null || pistaMúsica2 == null) return;

        reproductorMúsica.Stop();
        reproductorMúsica.clip = (indicePista == 1) ? pistaMúsica1 : pistaMúsica2;
        reproductorMúsica.time = tiempoInicio;
        reproductorMúsica.Play();
    }

    private System.Collections.IEnumerator Co_PosicionarConRetraso(ulong clientId, int ordenDeEntrada) {
        int intentos = 0;
        while (!NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId) && intentos < 30) {
            intentos++;
            yield return null;
        }
        yield return null;

        PosicionarJugadorPorOrdenReal(clientId, ordenDeEntrada);
    }

    private void OnListaPuntajesCambiara(NetworkListEvent<JugadorPuntaje> changeEvent) {
        ActualizarUI();
    }

    private void PosicionarJugadorPorOrdenReal(ulong clientId, int ordenDeEntrada) {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) return;

        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var networkClient)) {
            NetworkObject jugadorNetObj = networkClient.PlayerObject;

            if (jugadorNetObj != null) {
                Vector3 posicionInicial = Vector3.zero;
                Quaternion rotacionInicial = Quaternion.identity;

                switch (ordenDeEntrada) {
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
                    controller.CambiarColorCapsulaClientRpc(ordenDeEntrada);
                }
                else {
                    jugadorNetObj.transform.position = posicionInicial;
                    jugadorNetObj.transform.rotation = rotacionInicial;
                }
            }
        }
    }

    void Update() {
        bool activo = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening ? juegoActivo.Value : juegoActivoLocal;
        if (!activo) return;

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
        if (txtPuntajes == null) return;

        string textoPuntos = "Puntajes:\n";

        if (NetworkManager.Singleton == null || listaPuntajes == null || !NetworkManager.Singleton.IsListening) {
            txtPuntajes.text = textoPuntos;
            return;
        }

        try {
            if (listaPuntajes.Count == 0) {
                txtPuntajes.text = textoPuntos;
                return;
            }

            for (int i = 0; i < listaPuntajes.Count; i++) {
                string codigoColor = "#FFFFFF";

                switch (i) {
                    case 0: codigoColor = "#FF0000"; break;
                    case 1: codigoColor = "#FFFF00"; break;
                    case 2: codigoColor = "#3080FF"; break;
                    case 3: codigoColor = "#00FF00"; break;
                }

                textoPuntos += $"<color={codigoColor}>Jugador {i + 1}: {listaPuntajes[i].puntos} pts</color>\n";
            }
        }
        catch {
            // Protección contra desvinculaciones asincrónicas
        }

        txtPuntajes.text = textoPuntos;
    }

    [ServerRpc(RequireOwnership = false)]
    public void SumarPuntoServerRpc(ulong clientId) {
        if (NetworkManager.Singleton == null || !juegoActivo.Value) return;

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
        if (NetworkManager.Singleton == null || !juegoActivo.Value) return;

        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(victimaClientId, out var networkClient)) {
            if (networkClient.PlayerObject != null && networkClient.PlayerObject.TryGetComponent<PlayerController>(out var controller)) {
                if (controller.VerificarSiTieneObjetoServidor()) {
                    controller.ForzarPerdidaObjetoServidor();
                    SpawnItemAleatorio();
                }
            }
        }
    }

    // ACTUALIZADO: Detección de empates dinámicos y formateo correcto de textos
    void TerminarPartida() {
        juegoActivo.Value = false;
        int maxPuntos = -1;

        // 1. Buscamos cuál fue la puntuación más alta alcanzada
        for (int i = 0; i < listaPuntajes.Count; i++) {
            if (listaPuntajes[i].puntos > maxPuntos) {
                maxPuntos = listaPuntajes[i].puntos;
            }
        }

        // 2. Filtramos todos los jugadores que tengan esa puntuación máxima
        List<int> indicesGanadores = new List<int>();
        for (int i = 0; i < listaPuntajes.Count; i++) {
            if (listaPuntajes[i].puntos == maxPuntos) {
                indicesGanadores.Add(i); // Guardamos su índice visual (0, 1, 2, 3)
            }
        }

        string mensaje = "Empate sin puntos";

        if (maxPuntos > 0) {
            // Caso A: Hay un único ganador definitivo
            if (indicesGanadores.Count == 1) {
                int ganadorIndice = indicesGanadores[0];
                string codigoColor = ObtenerHexColorPorIndice(ganadorIndice);
                mensaje = $"Ganador: <color={codigoColor}>Jugador {ganadorIndice + 1}</color> con {maxPuntos} pts";
            }
            // Caso B: Hay un empate real entre dos o más jugadores con puntos
            else {
                mensaje = "¡Empate! ";
                for (int k = 0; k < indicesGanadores.Count; k++) {
                    int jugadorIndice = indicesGanadores[k];
                    string codigoColor = ObtenerHexColorPorIndice(jugadorIndice);

                    mensaje += $"<color={codigoColor}>Jugador {jugadorIndice + 1}</color>";

                    // Ponemos una "y" intermedia bien formateada si faltan más ganadores por agregar
                    if (k < indicesGanadores.Count - 1) {
                        mensaje += " y ";
                    }
                }
                mensaje += $" con {maxPuntos} pts";
            }
        }

        ApagarMúsicaClientRpc();
        MostrarFinJuegoClientRpc(mensaje);
    }

    // Método auxiliar interno para mapear los mismos strings de color de la UI de puntajes
    private string ObtenerHexColorPorIndice(int indice) {
        switch (indice) {
            case 0: return "#FF0000"; // Rojo
            case 1: return "#FFFF00"; // Amarillo
            case 2: return "#3080FF"; // Azul
            case 3: return "#00FF00"; // Verde
            default: return "#FFFFFF";
        }
    }

    [ClientRpc]
    private void ApagarMúsicaClientRpc() {
        if (reproductorMúsica != null) {
            reproductorMúsica.Stop();
        }
    }

    [ClientRpc]
    void MostrarFinJuegoClientRpc(string mensajeGanador) {
        if (txtGanador != null) txtGanador.text = mensajeGanador;
        if (panelFinDeJuego != null) panelFinDeJuego.SetActive(true);

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
        if (IsServer) ReiniciarPartida();
        else ReiniciarPartidaServerRpc();
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

        int index = 0;
        foreach (var id in NetworkManager.Singleton.ConnectedClientsIds) {
            StartCoroutine(Co_PosicionarConRetraso(id, index));
            index++;
        }

        while (listaPuntajes.Count > 0) {
            listaPuntajes.RemoveAt(0);
        }

        index = 0;
        foreach (var id in NetworkManager.Singleton.ConnectedClientsIds) {
            listaPuntajes.Add(new JugadorPuntaje { clientId = id, puntos = 0 });
            index++;
        }

        for (int i = 0; i < esferasIniciales; i++) {
            SpawnItemAleatorio();
        }

        nextSpawnTime = Time.time + 3f;
        tiempoRestante.Value = tiempoDeJuego;
        juegoActivo.Value = true;

        ElegirYReproducirMúsicaClientRpc();
        OcultarFinJuegoClientRpc();
    }

    [ClientRpc]
    private void ElegirYReproducirMúsicaClientRpc() {
        ElegirYReproducirMúsicaLocal();
    }

    [ClientRpc]
    void OcultarFinJuegoClientRpc() {
        panelFinDeJuego.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void SolicitarSalirAlMenu() {
        if (NetworkManager.Singleton == null) {
            DesconectarseLocalmente();
            return;
        }

        if (!NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsClient) {
            DesconectarseLocalmente();
            return;
        }

        if (IsServer) {
            List<ulong> remotos = ObtenerClientesRemotos();

            if (remotos.Count > 0) {
                ClientRpcParams parametrosOpciones = new ClientRpcParams {
                    Send = new ClientRpcSendParams {
                        TargetClientIds = remotos
                    }
                };
                ForzarSalidaMenuRemotoClientRpc(parametrosOpciones);
            }

            DesconectarseLocalmente();
        }
        else {
            if (NetworkManager.Singleton.IsClient) {
                SolicitarSalirAlMenuServerRpc();
            }
            else {
                DesconectarseLocalmente();
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SolicitarSalirAlMenuServerRpc() {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) return;

        List<ulong> remotos = ObtenerClientesRemotos();

        if (remotos.Count > 0) {
            ClientRpcParams parametrosOpciones = new ClientRpcParams {
                Send = new ClientRpcSendParams {
                    TargetClientIds = remotos
                }
            };
            ForzarSalidaMenuRemotoClientRpc(parametrosOpciones);
        }

        DesconectarseLocalmente();
    }

    private List<ulong> ObtenerClientesRemotos() {
        List<ulong> remotos = new List<ulong>();

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) {
            return remotos;
        }

        if (NetworkManager.Singleton.ConnectedClientsIds == null) {
            return remotos;
        }

        foreach (var id in NetworkManager.Singleton.ConnectedClientsIds) {
            if (id != NetworkManager.Singleton.LocalClientId) {
                remotos.Add(id);
            }
        }
        return remotos;
    }

    [ClientRpc]
    private void ForzarSalidaMenuRemotoClientRpc(ClientRpcParams clientRpcParams = default) {
        DesconectarseLocalmente();
    }

    private void DesconectarseLocalmente() {
        bool eraServidor = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        if (NetworkManager.Singleton != null) {
            if (eraServidor) {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClienteConectado;
            }
            NetworkManager.Singleton.Shutdown();
        }

        LimpiezaAbsolutaPorDesconexion(eraServidor);
    }

    private void LimpiezaAbsolutaPorDesconexion(bool limpiarLista) {
        juegoActivoLocal = false;

        if (reproductorMúsica != null) {
            reproductorMúsica.Stop();
        }

        if (limpiarLista && listaPuntajes != null) {
            try {
                while (listaPuntajes.Count > 0) {
                    listaPuntajes.RemoveAt(0);
                }
            }
            catch {
                // Captura sutil de desvinculaciones asincrónicas
            }
        }

        if (txtTiempo != null) txtTiempo.text = "Tiempo: --s";
        if (txtPuntajes != null) txtPuntajes.text = "Puntajes:\n";

        if (panelFinDeJuego != null) panelFinDeJuego.SetActive(false);
        if (canvasMenuPrincipal != null) canvasMenuPrincipal.SetActive(true);

        Camera camaraEscena = Camera.main;
        if (camaraEscena == null) {
            Camera[] todasLasCamaras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (todasLasCamaras.Length > 0) {
                todasLasCamaras[0].gameObject.SetActive(true);
            }
            else {
                GameObject camRespaldo = new GameObject("Camara_Menu_Respaldo");
                camRespaldo.AddComponent<Camera>();
                camRespaldo.transform.position = new Vector3(0f, 15f, -25f);
                camRespaldo.transform.rotation = Quaternion.Euler(25f, 0f, 0f);
            }
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}