using Unity.Netcode;
using UnityEngine;

public class NetworkItem : NetworkBehaviour {
    [ServerRpc(RequireOwnership = false)]
    public void RecogerObjetoServerRpc() {
        
        GetComponent<NetworkObject>().Despawn(true);
    }
}