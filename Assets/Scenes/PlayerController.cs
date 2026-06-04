using Unity.Netcode;
using UnityEngine;

public class PlayerController : NetworkBehaviour {
    [Header("Movimiento")]
    public float velocidad = 5f;

    [Header("Cámara y Sensibilidad")]
    public float sensibilidadMouse = 15f;
    public float limiteVerticalMin = -30f;
    public float limiteVerticalMax = 60f;

    private bool tieneObjeto = false;
    private GameObject objetoVisual;
    private MeshRenderer rendererEsferaCabeza; // Referencia interna para pintar la esfera sin romper el material
    private Camera camaraHija;
    private float rotacionX = 0f;

    // Variables internas
    private float inputX = 0f;
    private float inputZ = 0f;
    private float rotacionMouseX = 0f;
    private float rotacionMouseY = 0f;

    // SOLUCIÓN AL COLOR LOCAL: Variable de red persistente sincronizada automáticamente
    // -1 significa que todavía no se le asignó ningún color.
    private NetworkVariable<int> colorIndexNet = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    void Start() {
        // Esfera arriba de la cápsula
        objetoVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        objetoVisual.transform.SetParent(transform);
        objetoVisual.transform.localPosition = new Vector3(0, 1.5f, 0);
        objetoVisual.transform.localScale = Vector3.one * 0.4f;

        if (objetoVisual.TryGetComponent<Collider>(out Collider col)) {
            Destroy(col);
        }

        // Guardamos el renderer de la esferita para usarlo dinámicamente
        if (objetoVisual.TryGetComponent<MeshRenderer>(out var meshRender)) {
            rendererEsferaCabeza = meshRender;

            // HERENCIA DE MATERIAL: Le asignamos temporalmente el mismo material de la cápsula 
            // para asegurarnos de que use un Shader compatible con URP en la Build
            if (TryGetComponent<MeshRenderer>(out var cuerpoRender)) {
                rendererEsferaCabeza.material = cuerpoRender.material;
            }
        }

        objetoVisual.SetActive(false);
    }

    public override void OnNetworkSpawn() {
        base.OnNetworkSpawn();

        // 1. Nos suscribimos al evento de cambio de la variable de red
        colorIndexNet.OnValueChanged += AlCambiarValorColor;

        // 2. Si el servidor ya asignó un color antes de que termináramos de spawnear localmente, lo aplicamos ya mismo
        if (colorIndexNet.Value != -1) {
            AplicarColorVisual(colorIndexNet.Value);
        }

        if (objetoVisual != null) {
            objetoVisual.SetActive(false);
        }

        // Buscamos la cámara en el spawn de red, cuando ya se definieron las identidades
        camaraHija = GetComponentInChildren<Camera>();

        // CONTROL DE INSTANCIA INTEGRADO Y CORREGIDO:
        if (!IsOwner) {
            // Desactivamos el GameObject de la cámara por completo para evitar que se pise con la nuestra
            if (camaraHija != null) {
                camaraHija.gameObject.SetActive(false);
            }

            if (TryGetComponent<AudioListener>(out AudioListener audio)) {
                audio.enabled = false;
            }
        }
        else {
            // Si soy el dueño, me aseguro de tener MI cámara activa
            if (camaraHija != null) {
                camaraHija.gameObject.SetActive(true);
            }

            // Bloquea y oculta el puntero del ratón en la pantalla para que no se salga de la ventana al girar
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    public override void OnNetworkDespawn() {
        base.OnNetworkDespawn();
        // Buenas prácticas: nos desuscribimos al salir de la red para evitar errores en memoria
        colorIndexNet.OnValueChanged -= AlCambiarValorColor;
    }

    // Este método se activa automáticamente en todas las pantallas cuando la NetworkVariable cambia en el servidor
    private void AlCambiarValorColor(int valorAnterior, int valorNuevo) {
        AplicarColorVisual(valorNuevo);
    }

    // Lógica unificada para aplicar el color tanto a la cápsula como a su esferita
    private void AplicarColorVisual(int index) {
        Color colorAsignado = Color.white;

        switch (index) {
            case 0: colorAsignado = Color.red; break;      // JUGADOR 1: Rojo
            case 1: colorAsignado = Color.yellow; break;   // JUGADOR 2: Amarillo
            case 2: colorAsignado = Color.blue; break;     // JUGADOR 3: Azul
            case 3: colorAsignado = Color.green; break;    // JUGADOR 4: Verde
        }

        // Pintamos la cápsula usando la propiedad interna estándar de Shaders Lit (URP)
        if (TryGetComponent<MeshRenderer>(out var cuerpoRenderer)) {
            cuerpoRenderer.material.SetColor("_BaseColor", colorAsignado);
        }

        // Pintamos la esferita de la cabeza con el mismísimo color en simultáneo
        if (rendererEsferaCabeza != null) {
            rendererEsferaCabeza.material.SetColor("_BaseColor", colorAsignado);
        }
    }

    void Update() {
        // Evitamos que un jugador controle la cámara o guarde inputs de los demás
        if (!IsOwner) return;

        // ==========================================
        // 1. CAPTURA DE INPUTS DEL MOUSE
        // ==========================================
        if (UnityEngine.InputSystem.Mouse.current != null) {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            rotacionMouseX = mouse.delta.x.ReadValue() * sensibilidadMouse;
            rotacionMouseY = mouse.delta.y.ReadValue() * sensibilidadMouse;
        }

        // ==========================================
        // 2. CAPTURA DE INPUTS DEL TECLADO
        // ==========================================
        inputX = 0f;
        inputZ = 0f;

        if (UnityEngine.InputSystem.Keyboard.current != null) {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) inputZ = 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) inputZ = -1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) inputX = -1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) inputX = 1f;
        }
    }

    void FixedUpdate() {
        // Si no es nuestro propio jugador, no aplicamos ninguna transformación física
        if (!IsOwner) return;

        transform.Rotate(Vector3.up * rotacionMouseX * Time.fixedDeltaTime);

        if (camaraHija != null) {
            rotacionX -= rotacionMouseY * Time.fixedDeltaTime;
            rotacionX = Mathf.Clamp(rotacionX, limiteVerticalMin, limiteVerticalMax);
            camaraHija.transform.localRotation = Quaternion.Euler(rotacionX, 0f, 0f);
        }

        // Calcular y aplicar el desplazamiento relativo al espacio local (Space.Self)
        Vector3 movimiento = new Vector3(inputX, 0f, inputZ).normalized * velocidad * Time.fixedDeltaTime;
        transform.Translate(movimiento, Space.Self);

        // Limpiamos los deltas del mouse para evitar rotación infinita si el mouse se queda quieto
        rotacionMouseX = 0f;
        rotacionMouseY = 0f;
    }

    private void OnTriggerEnter(Collider other) {
        // CORRECCIÓN CLAVE: El Servidor procesa de manera exclusiva y autoritativa las colisiones
        if (!IsServer) return;

        // ==========================================
        // 1. RECOGIDA DEL ÍTEM
        // ==========================================
        if (other.CompareTag("Item") && !tieneObjeto) {
            NetworkObject netObj = other.GetComponent<NetworkObject>();
            if (netObj != null) {
                tieneObjeto = true;

                // Sincronizamos la esfera visual en todas las pantallas
                CambiarEstadoObjetoVisualClientRpc(true);

                // Remueve de forma segura el objeto físico de la red
                netObj.Despawn(true);
            }
        }

        // ==========================================
        // 2. ENTREGA EN LA BASE CENTRAL
        // ==========================================
        if (other.CompareTag("ZonaEntrega") && tieneObjeto) {
            tieneObjeto = false;

            // Apagamos el indicador visual para todos
            CambiarEstadoObjetoVisualClientRpc(false);

            // Le enviamos al GameManager el OwnerClientId real verificado por el Host
            GameManager.Instance.SumarPuntoServerRpc(OwnerClientId);
        }
    }

    // ClientRpc para prender/apagar el objeto visual arriba de la cápsula de forma sincronizada
    [ClientRpc]
    private void CambiarEstadoObjetoVisualClientRpc(bool activar) {
        if (objetoVisual != null) {
            objetoVisual.SetActive(activar);
        }
    }

    private void OnDisable() {
        if (IsOwner) {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    // Método para limpiar esferas fantasmas al spawnear o reiniciar
    public void ResetearEstadoJugador() {
        tieneObjeto = false;
        if (objetoVisual != null) {
            objetoVisual.SetActive(false);
        }
    }

    // CORRECCIÓN UNIVERSAL: El método corre sin parámetros en la etiqueta para evitar conflictos de compilación
    [ClientRpc]
    public void TeletransportarSeguroClientRpc(Vector3 nuevaPosicion, Quaternion nuevaRotacion) {
        // Validación clave: solo si somos el dueño de esta cápsula ejecutamos el Teleport
        if (IsOwner) {
            if (TryGetComponent<ClientNetworkTransform>(out var clientTransform)) {
                clientTransform.Teleport(nuevaPosicion, nuevaRotacion, transform.localScale);
            }
            else {
                transform.position = nuevaPosicion;
                transform.rotation = nuevaRotacion;
            }
        }
    }

    // Mantenemos el método para que el GameManager le mande la señal al Servidor, 
    // pero ahora en lugar de pintar directo, actualiza la NetworkVariable
    [ClientRpc]
    public void CambiarColorCapsulaClientRpc(int colorIndex) {
        if (IsServer) {
            colorIndexNet.Value = colorIndex;
        }
    }
}