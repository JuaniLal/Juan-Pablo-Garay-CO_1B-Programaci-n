using Unity.Netcode;
using UnityEngine;

public class PlayerController : NetworkBehaviour {
    [Header("Movimiento")]
    public float velocidad = 5f;
    public float fuerzaSalto = 6f;

    [Header("Combate")]
    public float rangoAtaque = 2f;
    public float radioHitbox = 1.2f;

    [Header("Cámara y Sensibilidad")]
    public float sensibilidadMouse = 15f;
    public float limiteVerticalMin = -30f;
    public float limiteVerticalMax = 60f;

    private bool tieneObjeto = false;
    private GameObject objetoVisual;
    private MeshRenderer rendererEsferaCabeza;
    private Camera camaraHija;
    private float rotacionX = 0f;
    private Rigidbody rb;

    // Variables  input
    private float inputX = 0f;
    private float inputZ = 0f;
    private float rotacionMouseX = 0f;
    private float rotacionMouseY = 0f;
    private bool deseoSaltar = false;

    private NetworkVariable<int> colorIndexNet = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    void Start() {
        rb = GetComponent<Rigidbody>();

        // Esfera arriba de la cápsula
        objetoVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        objetoVisual.transform.SetParent(transform);
        objetoVisual.transform.localPosition = new Vector3(0, 1.5f, 0);
        objetoVisual.transform.localScale = Vector3.one * 0.4f;

        if (objetoVisual.TryGetComponent<Collider>(out Collider col)) {
            Destroy(col);
        }

        if (objetoVisual.TryGetComponent<MeshRenderer>(out var meshRender)) {
            rendererEsferaCabeza = meshRender;
            if (TryGetComponent<MeshRenderer>(out var cuerpoRender)) {
                rendererEsferaCabeza.material = cuerpoRender.material;
            }
        }

        objetoVisual.SetActive(false);
    }

    public override void OnNetworkSpawn() {
        base.OnNetworkSpawn();

        colorIndexNet.OnValueChanged += AlCambiarValorColor;

        if (colorIndexNet.Value != -1) {
            AplicarColorVisual(colorIndexNet.Value);
        }

        if (objetoVisual != null) {
            objetoVisual.SetActive(false);
        }

        camaraHija = GetComponentInChildren<Camera>();

        if (!IsOwner) {
            if (camaraHija != null) {
                camaraHija.gameObject.SetActive(false);
            }
            if (TryGetComponent<AudioListener>(out AudioListener audio)) {
                audio.enabled = false;
            }
        }
        else {
            if (camaraHija != null) {
                camaraHija.gameObject.SetActive(true);
            }
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    public override void OnNetworkDespawn() {
        base.OnNetworkDespawn();
        colorIndexNet.OnValueChanged -= AlCambiarValorColor;
    }

    private void AlCambiarValorColor(int valorAnterior, int valorNuevo) {
        AplicarColorVisual(valorNuevo);
    }

    private void AplicarColorVisual(int index) {
        Color colorAsignado = Color.white;

        switch (index) {
            case 0: colorAsignado = Color.red; break;
            case 1: colorAsignado = Color.yellow; break;
            case 2: colorAsignado = Color.blue; break;
            case 3: colorAsignado = Color.green; break;
        }

        if (TryGetComponent<MeshRenderer>(out var cuerpoRenderer)) {
            cuerpoRenderer.material.SetColor("_BaseColor", colorAsignado);
        }

        if (rendererEsferaCabeza != null) {
            rendererEsferaCabeza.material.SetColor("_BaseColor", colorAsignado);
        }
    }

    void Update() {
        if (!IsOwner) return;

        
        if (UnityEngine.InputSystem.Mouse.current != null) {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            rotacionMouseX = mouse.delta.x.ReadValue() * sensibilidadMouse;
            rotacionMouseY = mouse.delta.y.ReadValue() * sensibilidadMouse;

            // aTAQUE
            if (mouse.leftButton.wasPressedThisFrame) {
                AtacarServerRpc();
            }
        }

        
        inputX = 0f;
        inputZ = 0f;

        if (UnityEngine.InputSystem.Keyboard.current != null) {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) inputZ = 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) inputZ = -1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) inputX = -1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) inputX = 1f;

            
            if (keyboard.spaceKey.wasPressedThisFrame && EstaEnElSuelo()) {
                deseoSaltar = true;
            }
        }
    }

    void FixedUpdate() {
        if (!IsOwner) return;

        transform.Rotate(Vector3.up * rotacionMouseX * Time.fixedDeltaTime);

        if (camaraHija != null) {
            rotacionX -= rotacionMouseY * Time.fixedDeltaTime;
            rotacionX = Mathf.Clamp(rotacionX, limiteVerticalMin, limiteVerticalMax);
            camaraHija.transform.localRotation = Quaternion.Euler(rotacionX, 0f, 0f);
        }

        // Desplazamiento relativo al espacio local
        Vector3 movimiento = new Vector3(inputX, 0f, inputZ).normalized * velocidad * Time.fixedDeltaTime;
        transform.Translate(movimiento, Space.Self);

       
        if (deseoSaltar) {
            rb.AddForce(Vector3.up * fuerzaSalto, ForceMode.Impulse);
            deseoSaltar = false;
        }

        rotacionMouseX = 0f;
        rotacionMouseY = 0f;
    }

    private bool EstaEnElSuelo() {
        
        float radioDeteccionSuelo = 0.3f;
        Vector3 puntoSuelo = transform.position + Vector3.down * 0.9f;
        Collider[] colliders = Physics.OverlapSphere(puntoSuelo, radioDeteccionSuelo);

        foreach (var col in colliders) {
            if (col.gameObject != gameObject) return true;
        }
        return false;
    }

    [ServerRpc]
    private void AtacarServerRpc() {
        // Hitbox al frente
        Vector3 centroHitbox = transform.position + transform.forward * rangoAtaque;

        
        Collider[] golpeados = Physics.OverlapSphere(centroHitbox, radioHitbox);

        foreach (var col in golpeados) {
            if (col.gameObject != gameObject && col.CompareTag("Player")) {
                if (col.TryGetComponent<PlayerController>(out var enemigo)) {
                    // El servidor reporta directamente el impacto del golpe al GameManager
                    GameManager.Instance.NotificarGolpeServerRpc(enemigo.OwnerClientId);
                }
            }
        }
    }

    private void OnTriggerEnter(Collider other) {
        if (!IsServer) return;

        //  RECOGE ÍTEM
        if (other.CompareTag("Item") && !tieneObjeto) {
            NetworkObject netObj = other.GetComponent<NetworkObject>();
            if (netObj != null) {
                tieneObjeto = true;
                CambiarEstadoObjetoVisualClientRpc(true);
                netObj.Despawn(true);
            }
        }

        //  ENTREGA EN LA BASE CENTRAL
        if (other.CompareTag("ZonaEntrega") && tieneObjeto) {
            tieneObjeto = false;
            CambiarEstadoObjetoVisualClientRpc(false);
            GameManager.Instance.SumarPuntoServerRpc(OwnerClientId);
        }
    }

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

    public void ResetearEstadoJugador() {
        tieneObjeto = false;
        if (objetoVisual != null) {
            objetoVisual.SetActive(false);
        }
    }

    public bool VerificarSiTieneObjetoServidor() {
        return tieneObjeto;
    }

    public void ForzarPerdidaObjetoServidor() {
        if (!IsServer) return;
        tieneObjeto = false;
        CambiarEstadoObjetoVisualClientRpc(false);
    }

    [ClientRpc]
    public void TeletransportarSeguroClientRpc(Vector3 nuevaPosicion, Quaternion nuevaRotacion) {
        if (IsOwner) {
            // posicionamiento 
            if (TryGetComponent<ClientNetworkTransform>(out var clientTransform)) {
                clientTransform.Teleport(nuevaPosicion, nuevaRotacion, transform.localScale);
            }
            else {
                transform.position = nuevaPosicion;
                transform.rotation = nuevaRotacion;
            }

            // limpia inercia residual del rigidbody :3
            if (TryGetComponent<Rigidbody>(out var rigidbodyLocal)) {
                rigidbodyLocal.linearVelocity = Vector3.zero;
                rigidbodyLocal.angularVelocity = Vector3.zero; // RESET
            }
        }
    }

    [ClientRpc]
    public void CambiarColorCapsulaClientRpc(int colorIndex) {
        if (IsServer) {
            colorIndexNet.Value = colorIndex;
        }
    }

    // guizmo pa ver la hitbox
    private void OnDrawGizmosSelected() {
        Gizmos.color = Color.red;
        Vector3 centroHitbox = transform.position + transform.forward * rangoAtaque;
        Gizmos.DrawWireSphere(centroHitbox, radioHitbox);
    }
}