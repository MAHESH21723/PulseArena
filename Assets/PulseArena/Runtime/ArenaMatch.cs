using Unity.Netcode;
using UnityEngine;
using System.Linq;

namespace PulseArena
{
    public class ArenaMatch:NetworkBehaviour
    {
        public static ArenaMatch Instance;
        public readonly NetworkVariable<int> Phase=new NetworkVariable<int>(0);
        public readonly NetworkVariable<double> EndsAt=new NetworkVariable<double>(0);
        public const int Goal=15;
        public override void OnNetworkSpawn(){Instance=this;Phase.OnValueChanged+=Changed;Changed(-1,Phase.Value);}
        public override void OnNetworkDespawn(){Phase.OnValueChanged-=Changed;if(Instance==this)Instance=null;}
        void Changed(int before,int after){if(PulseApp.Instance)PulseApp.Instance.OnPhase(after);}
        void Update(){if(IsServer&&Phase.Value==1&&NetworkManager.ServerTime.Time>=EndsAt.Value)Phase.Value=2;}
        public void StartRound()
        {
            if(!IsServer)return;
            foreach(var player in FindObjectsByType<ArenaPlayer>(FindObjectsSortMode.None))player.ResetForRound();
            EndsAt.Value=NetworkManager.ServerTime.Time+180;Phase.Value=1;
        }
        public void CheckWinner(){if(IsServer&&Players().Any(p=>p.Kills.Value>=Goal))Phase.Value=2;}
        public static ArenaPlayer[] Players()=>FindObjectsByType<ArenaPlayer>(FindObjectsSortMode.None).OrderByDescending(p=>p.Kills.Value).ThenBy(p=>p.Deaths.Value).ThenBy(p=>p.CallSign.Value.ToString()).ToArray();
    }
}
