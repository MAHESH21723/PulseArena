using System.Collections.Generic;
using UnityEngine;

namespace PulseArena
{
    public static class ArenaArt
    {
        public static readonly Color Cyan = new Color(.16f, .92f, 1f);
        public static readonly Color Amber = new Color(1f, .51f, .17f);
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        public static Material Mat(string name)
        {
            if (!materials.TryGetValue(name, out var m) || !m)
                materials[name] = m = Resources.Load<Material>("Materials/" + name);
            return m;
        }
        public static GameObject Shape(string name, PrimitiveType type, Transform parent, Vector3 pos, Vector3 size, string material, bool solid = false)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name;
            go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = Mat(material);
            if (!solid) { var c = go.GetComponent<Collider>(); c.enabled = false; Object.Destroy(c); }
            return go;
        }
        static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, string mat, bool solid = true)
            => Shape(name, PrimitiveType.Cube, parent, pos, size, mat, solid);

        public static void BuildArena(Transform root)
        {
            Box("Foundation", root, new Vector3(0,-.4f,0), new Vector3(48,.8f,48), "Floor");
            for (int i=-22;i<=22;i+=4)
            {
                Box("Floor seam",root,new Vector3(i,.005f,0),new Vector3(.025f,.008f,46),"Seam",false);
                Box("Floor seam",root,new Vector3(0,.005f,i),new Vector3(46,.008f,.025f),"Seam",false);
            }
            for(int side=0;side<4;side++)
            {
                var wall = new GameObject("Perimeter " + side).transform;wall.SetParent(root,false);wall.localRotation=Quaternion.Euler(0,side*90,0);
                Box("Wall",wall,new Vector3(0,2.2f,24),new Vector3(49,4.4f,1),"Wall");
                Box("Light rail",wall,new Vector3(0,3.55f,23.45f),new Vector3(47,.12f,.08f),side%2==0?"Cyan":"Amber",false);
                Box("Base rail",wall,new Vector3(0,.2f,23.4f),new Vector3(47,.1f,.1f),"Steel",false);
                for(int x=-20;x<=20;x+=8)
                {
                    Box("Buttress",wall,new Vector3(x,3,23.5f),new Vector3(.8f,6,1.1f),"Steel");
                    Box("Signal",wall,new Vector3(x,4.5f,22.9f),new Vector3(.15f,1.1f,.1f),"Cyan",false);
                }
            }
            foreach(var p in new[]{new Vector3(-9,0,-8),new Vector3(9,0,8),new Vector3(-9,0,8),new Vector3(9,0,-8)})
            {
                var cover=new GameObject("Reactor cover").transform;cover.SetParent(root,false);cover.localPosition=p;
                Box("Hull",cover,new Vector3(0,1.25f,0),new Vector3(5,2.5f,3),"Wall");
                Box("Top armor",cover,new Vector3(0,2.5f,0),new Vector3(5.25f,.22f,3.25f),"Steel");
                for(int x=-2;x<=2;x++) Box("Vent",cover,new Vector3(x,1.4f,-1.52f),new Vector3(.35f,1.35f,.06f),"Floor",false);
                Box("Power strip",cover,new Vector3(0,2.13f,-1.56f),new Vector3(4.5f,.09f,.08f),p.x*p.z>0?"Cyan":"Amber",false);
                Box("Power strip",cover,new Vector3(0,2.13f,1.56f),new Vector3(4.5f,.09f,.08f),p.x*p.z>0?"Cyan":"Amber",false);
            }
            for(int i=0;i<4;i++)
            {
                var p=Quaternion.Euler(0,i*90,0)*new Vector3(0,0,16);
                Box("Low barricade",root,p+Vector3.up*.65f,new Vector3(i%2==0?6:1.5f,1.3f,i%2==0?1.5f:6),"Steel");
            }
            Shape("Reactor plinth",PrimitiveType.Cylinder,root,new Vector3(0,.1f,0),new Vector3(6,.1f,6),"Steel",true);
            Shape("Core shield",PrimitiveType.Cylinder,root,new Vector3(0,1.35f,0),new Vector3(2.8f,1.25f,2.8f),"Wall",true);
            Shape("Core glow",PrimitiveType.Cylinder,root,new Vector3(0,2.9f,0),new Vector3(1.45f,.3f,1.45f),"Cyan");
            Shape("Reactor cap",PrimitiveType.Cylinder,root,new Vector3(0,3.4f,0),new Vector3(3,.15f,3),"Steel");
            for(int i=0;i<12;i++)
            {
                float a=i*Mathf.PI/6;var p=new Vector3(Mathf.Cos(a)*4,.02f,Mathf.Sin(a)*4);
                var marker=Box("Core marker",root,p,new Vector3(.2f,.02f,1),"Cyan",false);marker.transform.localRotation=Quaternion.Euler(0,-i*30,0);
            }
            for(int i=0;i<4;i++)
            {
                Vector3 p=Spawn(i);
                Shape("Spawn pad",PrimitiveType.Cylinder,root,p+Vector3.up*.015f,new Vector3(3,.015f,3),i%2==0?"Cyan":"Amber");
                for(int k=0;k<3;k++) Box("Pad inset",root,p+new Vector3(k*.5f-.5f,.05f,0),new Vector3(.25f,.02f,1.5f),"Floor",false);
            }
            // A distant skyline gives the enclosed arena a sense of place.
            var random = new System.Random(19);
            for(int side=0;side<4;side++) for(int i=0;i<8;i++)
            {
                float h=8+(float)random.NextDouble()*18;
                var q=Quaternion.Euler(0,side*90,0);var p=q*new Vector3(-40+i*11,h*.5f,40+(float)random.NextDouble()*12);
                Box("Distant tower",root,p,new Vector3(6,h,6),"Skyline",false);
                for(int f=3;f<h;f+=3) Box("Skyline window",root,p-new Vector3(0,h*.5f-f,0)+q*new Vector3(0,0,-3.05f),q*Vector3.right==Vector3.right?new Vector3(3,.08f,.06f):new Vector3(.08f,.08f,3),"Seam",false);
            }
        }
        public static Vector3 Spawn(int index)
        {
            var spots=new[]{new Vector3(-18,.1f,-18),new Vector3(18,.1f,18),new Vector3(-18,.1f,18),new Vector3(18,.1f,-18)};
            return spots[Mathf.Abs(index)%spots.Length];
        }
        public static Transform Robot(Transform root,string glow)
        {
            var model=new GameObject("Sentinel chassis").transform;model.SetParent(root,false);
            Shape("Torso",PrimitiveType.Capsule,model,new Vector3(0,1.13f,0),new Vector3(.64f,.38f,.44f),"Steel");
            Box("Chest armor",model,new Vector3(0,1.24f,.22f),new Vector3(.62f,.4f,.18f),"Armor",false);
            Box("Chest reactor",model,new Vector3(0,1.25f,.32f),new Vector3(.12f,.24f,.03f),glow,false);
            Shape("Head",PrimitiveType.Sphere,model,new Vector3(0,1.75f,0),new Vector3(.47f,.44f,.43f),"Armor");
            Box("Visor",model,new Vector3(0,1.78f,.21f),new Vector3(.4f,.11f,.08f),glow,false);
            for(int side=-1;side<=1;side+=2)
            {
                var leg=Box(side<0?"LeftLeg":"RightLeg",model,new Vector3(side*.21f,.51f,0),new Vector3(.25f,.85f,.28f),"Armor",false);
                Box("Knee",leg.transform,new Vector3(0,0,.55f),new Vector3(.8f,.25f,.35f),"Steel",false);
                Box("Shoulder",model,new Vector3(side*.47f,1.45f,0),new Vector3(.29f,.28f,.36f),"Armor",false);
                var arm=Box("Arm",model,new Vector3(side*.43f,1.13f,.15f),new Vector3(.2f,.54f,.22f),"Steel",false);arm.transform.localRotation=Quaternion.Euler(-30,0,0);
            }
            var gun=Weapon(model);gun.localPosition=new Vector3(.32f,1.13f,.42f);gun.localScale=Vector3.one*.65f;
            return model;
        }
        public static Transform Weapon(Transform parent)
        {
            var root=new GameObject("PX-24 Pulse Carbine").transform;root.SetParent(parent,false);
            Box("Receiver",root,Vector3.zero,new Vector3(.19f,.2f,.62f),"Armor",false);
            Box("Upper rail",root,new Vector3(0,.125f,.04f),new Vector3(.15f,.05f,.58f),"Steel",false);
            Box("Stock",root,new Vector3(0,-.04f,-.39f),new Vector3(.14f,.24f,.24f),"Steel",false);
            var grip=Box("Grip",root,new Vector3(0,-.18f,-.1f),new Vector3(.11f,.25f,.13f),"Floor",false);grip.transform.localRotation=Quaternion.Euler(-15,0,0);
            Box("Cell",root,new Vector3(0,-.19f,.12f),new Vector3(.13f,.2f,.15f),"Steel",false);
            Box("Cell charge",root,new Vector3(.072f,-.15f,.12f),new Vector3(.015f,.11f,.1f),"Cyan",false);
            var barrel=Shape("Barrel",PrimitiveType.Cylinder,root,new Vector3(0,.015f,.44f),new Vector3(.105f,.16f,.105f),"Steel");barrel.transform.localRotation=Quaternion.Euler(90,0,0);
            var muzzle=Shape("Muzzle",PrimitiveType.Cylinder,root,new Vector3(0,.015f,.61f),new Vector3(.16f,.045f,.16f),"Floor");muzzle.transform.localRotation=Quaternion.Euler(90,0,0);
            Box("Sight rear",root,new Vector3(0,.19f,-.1f),new Vector3(.14f,.09f,.04f),"Floor",false);
            Box("Sight diode",root,new Vector3(0,.2f,.22f),new Vector3(.035f,.05f,.04f),"Cyan",false);
            for(int i=0;i<5;i++)Box("Heat sink",root,new Vector3(.102f,.01f,.18f-i*.075f),new Vector3(.014f,.1f,.025f),"Cyan",false);
            return root;
        }
    }
}
