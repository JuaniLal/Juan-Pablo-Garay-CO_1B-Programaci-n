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

            // MODIFICACIÓN: Población inicial del mapa de forma segura en el arranque
            for (int i = 0; i < esferasIniciales; i++) {
                SpawnItemAleatorio();
            }

            // Retraso inicial de 4 segundos al arrancar la red para el spawn progresivo posterior
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

                // DISTRIBUCIÓN EN CRUZ SIMÉTRICA (Norte, Sur, Este, Oeste)
                switch (clientId) {
                    case 0:
                        // JUGADOR 1: Extremo Sur - Mirando al Norte
                        posicionInicial = new Vector3(0f, 1f, -35f);
                        rotacionInicial = Quaternion.Euler(0f, 0f, 0f);
                        break;
                    case 1:
                        // JUGADOR 2: Extremo Norte - Mirando al Sur
                        posicionInicial = new Vector3(0f, 1f, 35f);
                        rotacionInicial = Quaternion.Euler(0f, 180f, 0f);
                        break;
                    case 2:
                        // JUGADOR 3: Extremo Oeste - Mirando al Este
                        posicionInicial = new Vector3(-35f, 1f, 0f);
                        rotacionInicial = Quaternion.Euler(0f, 90f, 0f);
                        break;
                    case 3:
                        // JUGADOR 4: Extremo Este - Mirando al Oeste
                        posicionInicial = new Vector3(35f, 1f, 0f);
                        rotacionInicial = Quaternion.Euler(0f, -90f, 0f);
                        break;
                    default:
                        // Fallback por si entran más de 4
                        float randomX = Random.Range(xMinimo * 0.5f, xMaximo * 0.5f);
                        float randomZ = Random.Range(zMinimo * 0.5f, zMaximo * 0.5f);
                        posicionInicial = new Vector3(randomX, 1f, randomZ);
                        rotacionInicial = Quaternion.identity;
                        break;
                }

                if (jugadorNetObj.TryGetComponent<PlayerController>(out var controller)) {
                    controller.ResetearEstadoJugador();
                    controller.TeletransportarSeguroClientRpc(posicionInicial, rotacionInicial);

                    // CORRECCIÓN: Le ordenamos a todas las pantallas que pinten este clon según su ID
                    // Pasamos (int)clientId porque nuestro switch de colores va del 0 al 3
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
    [Tooltip("Distancia mínima desde el centro (0,0,0) donde pueden spawnear los ítems. Ajustalo según el tamaño de tu base.")]
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
            // Determinamos el tag de color según el clientId (0=Rojo, 1=Amarillo, 2=Azul, 3=Verde)
            string codigoColor = "#FFFFFF"; // Blanco por defecto si entra un 5to jugador

            switch (jp.clientId) {
                case 0: codigoColor = "#FF0000"; break; // Jugador 1: Rojo
                case 1: codigoColor = "#FFFF00"; break; // Jugador 2: Amarillo
                case 2: codigoColor = "#3080FF"; break; // Jugador 3: Azul (Tono legible)
                case 3: codigoColor = "#00FF00"; break; // Jugador 4: Verde
            }

            // Formateamos la línea usando la etiqueta <color=HEX>Texto</color> de TextMeshPro
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

        string mensaje = maxPuntos > -1 ? $"Ganador: Jugador {ganadorId + 1} con {maxPuntos} pts" : "Empate sin puntos";
        MostrarFinJuegoClientRpc(mensaje);
    }

    [ClientRpc]
    void MostrarFinJuegoClientRpc(string mensajeGanador) {
        txtGanador.text = mensajeGanador;
        panelFinDeJuego.SetActive(true);

        if (btnReiniciar != null) {
            btnReiniciar.onClick.RemoveAllListeners();
            btnReiniciar.onClick.AddListener(IntentarReiniciarPartida);
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

        NetworkItem[] items = FindObjectsByType<NetworkItem>(FindObjectsSortMode.None);
        foreach (var item in items) {
            if (item.GetComponent<NetworkObject>().IsSpawned) {
                item.GetComponent<NetworkObject>().Despawn(true);
            }
        }

        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds) {
            PosicionarJugador(clientId);
        }

        for (int i = 0; i < listaPuntajes.Count; i++) {
            var reseteado = listaPuntajes[i];
            reseteado.puntos = 0;
            listaPuntajes[i] = reseteado;
        }

        // MODIFICACIÓN: Volvemos a repoblar la arena con 30 esferas al resetear
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
}