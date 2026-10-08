using UnityEngine;

namespace PulseArena
{
    public class PulseAudio : MonoBehaviour
    {
        public static PulseAudio Instance;
        AudioSource source, ambience;
        AudioClip shot, hit, reload, click, kill, respawn;
        void Awake()
        {
            Instance=this; source=gameObject.AddComponent<AudioSource>();source.spatialBlend=0;
            shot=Synth("Pulse shot",.16f,180,35,.65f);hit=Synth("Shield hit",.1f,880,440,.25f);
            reload=Synth("Cell charge",.45f,120,1000,.08f);click=Synth("Interface",.065f,760,520,.03f);
            kill=Synth("Elimination",.35f,380,1100,.12f);respawn=Synth("Materialize",.7f,80,600,.08f);
            ambience=gameObject.AddComponent<AudioSource>();ambience.loop=true;ambience.clip=Ambient();ambience.volume=.18f;ambience.Play();
            AudioListener.volume=PlayerPrefs.GetFloat("volume",.7f);
        }
        static AudioClip Synth(string name,float seconds,float from,float to,float noise)
        {
            const int rate=22050;var data=new float[(int)(rate*seconds)];var rand=new System.Random(name.Length);float phase=0;
            for(int i=0;i<data.Length;i++)
            {float t=(float)i/data.Length;phase+=Mathf.Lerp(from,to,t)*2*Mathf.PI/rate;float envelope=Mathf.Sin(Mathf.PI*Mathf.Min(t*15,1))*.1f+Mathf.Pow(1-t,2)*Mathf.Min(t*70,1);
                data[i]=(Mathf.Sin(phase)*.5f+Mathf.Sin(phase*.5f)*.15f+((float)rand.NextDouble()*2-1)*noise)*envelope*.65f;}
            var clip=AudioClip.Create(name,data.Length,1,rate,false);clip.SetData(data,0);return clip;
        }
        static AudioClip Ambient()
        {
            const int rate=22050;var data=new float[rate*8];
            for(int i=0;i<data.Length;i++){float t=(float)i/rate; data[i]=(Mathf.Sin(t*2*Mathf.PI*55)*.12f+Mathf.Sin(t*2*Mathf.PI*82.5f)*.07f+Mathf.Sin(t*2*Mathf.PI*110)*.03f)*(.7f+.3f*Mathf.Cos(t*Mathf.PI/4));}
            var clip=AudioClip.Create("Reactor atmosphere",data.Length,1,rate,false);clip.SetData(data,0);return clip;
        }
        public void Play(string kind,float volume=1)
        {var clip=kind=="shot"?shot:kind=="hit"?hit:kind=="reload"?reload:kind=="kill"?kill:kind=="respawn"?respawn:click;source.PlayOneShot(clip,volume);}
    }
    public class PulseEffect : MonoBehaviour
    {
        float time, life; Vector3 startScale; Renderer rend;
        public static void Impact(Vector3 point,bool shield)
        {
            var go=ArenaArt.Shape("Impact",PrimitiveType.Sphere,null,point,Vector3.one*.13f,shield?"Cyan":"Amber");
            var effect=go.AddComponent<PulseEffect>();effect.life=.22f;effect.startScale=go.transform.localScale;
        }
        public static void Trace(Vector3 from,Vector3 to)
        {
            var go=new GameObject("Pulse tracer");var line=go.AddComponent<LineRenderer>();line.sharedMaterial=ArenaArt.Mat("Cyan");line.positionCount=2;line.SetPosition(0,from);line.SetPosition(1,to);line.startWidth=.028f;line.endWidth=.006f;
            var effect=go.AddComponent<PulseEffect>();effect.life=.065f;
        }
        void Update(){time+=Time.deltaTime;if(time>=life){Destroy(gameObject);return;}if(startScale!=Vector3.zero)transform.localScale=startScale*(1+time/life*2);}
    }
}
