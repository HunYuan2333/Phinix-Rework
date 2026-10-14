using System;using System.Collections.Generic;using UnityEngine;using UnityStubs.Gui;using PhinixClient.Framework;
internal static class Program
{
    private static int checks;
    private static void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
    private static float Cross(Vector2 a,Vector2 b,Vector2 c)=>(b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);
    private static bool Contains(Vector2 a,Vector2 b,Vector2 c,Vector2 point)
    { if(Math.Abs(Cross(a,b,c))<.0001)return false;float x=Cross(a,b,point),y=Cross(b,c,point),z=Cross(c,a,point);return x>=-.0001&&y>=-.0001&&z>=-.0001 || x<=.0001&&y<=.0001&&z<=.0001; }
    private static void Main()
    {
        var previous=GuiBackend.Current;var previousEvent=Event.current;var backend=new Backend();
        try
        {
            GuiBackend.Current=backend;Event.current=new Event{type=EventType.Repaint};var rect=new Rect(10,20,100,60);var color=new Color(.2f,.3f,.4f,.5f);
            ClientGameDrawing.RoundedSurface(rect,color,12);Check(backend.Fills==1&&backend.Radius==12&&backend.FillColor.a==.5f,"Native rounded fill receives explicit theme alpha and radius.");
            foreach(var spec in new[]{(0f,1f),(12f,1f),(4f,6f)})
            {
                ClientGameDrawing.RoundedBorder(rect,color,spec.Item1,spec.Item2);Check(backend.Mesh!=null&&backend.Mesh.Length%6==0&&backend.Mode==GL.TRIANGLES&&!backend.ScreenSpace&&backend.Texture==null,"Native local-coordinate triangle submission.");
                var center=backend.ToScreen(new Vector2(rect.x+rect.width/2,rect.y+rect.height/2));double area=0;
                for(int i=0;i<backend.RenderedMesh.Length;i+=3)
                {
                    var a=backend.RenderedMesh[i].position;var b=backend.RenderedMesh[i+1].position;var c=backend.RenderedMesh[i+2].position;
                    Check(!Contains(a,b,c,center),"Transparent center must not be painted.");area+=Math.Abs(Cross(a,b,c))/2;
                }
                double r=spec.Item1,t=spec.Item2,ir=Math.Max(0,r-t);double expected=(rect.width*rect.height-(4-Math.PI)*r*r-((rect.width-2*t)*(rect.height-2*t)-(4-Math.PI)*ir*ir))*3;
                Check(Math.Abs(area-expected)<expected*.02,"Ring area matches rounded outer minus inner region under coordinate scaling.");
                foreach(var v in backend.RenderedMesh){Check(v.position.x>=220-.001&&v.position.x<=420+.001&&v.position.y>=330-.001&&v.position.y<=420+.001,"Backend rendering applies group offset before matrix, like native fill.");Check(v.color.a>=127&&v.color.a<=128,"Theme alpha reaches native Color32 vertices.");}
                Check(backend.GuiConversions==0,"Image/input coordinate conversion is never substituted for native drawing transform.");
                Check(backend.Fills==1,"Border never simulates a hole by overwriting its interior.");
            }
            var held=backend.Mesh;var first=held[0].position;ClientGameDrawing.RoundedBorder(new Rect(30,40,80,40),color,8,2);Check(!ReferenceEquals(held,backend.Mesh)&&held[0].position.x==first.x,"Submissions do not mutate an earlier mesh.");
            int calls=backend.Fills+backend.MeshCalls;
            foreach(var type in new[]{EventType.Layout,EventType.KeyDown,EventType.MouseDown}){Event.current.type=type;ClientGameDrawing.RoundedSurface(rect,color,12);ClientGameDrawing.RoundedBorder(rect,color,12,1);}Check(calls==backend.Fills+backend.MeshCalls,"Input and measurement never paint.");
            Event.current.type=EventType.Repaint;GuiBackend.Current=null;ClientGameDrawing.RoundedSurface(rect,color,12);Check(calls==backend.Fills+backend.MeshCalls,"Missing backend preserves startup no-op.");GuiBackend.Current=backend;
            ClientGameDrawing.RoundedBorder(new Rect(0,0,10,10),color,5,5);Check(backend.Fills==2,"Collapsed inner region correctly uses a full rounded fill.");
            backend.Fail=true;bool failed=false;try{ClientGameDrawing.RoundedSurface(rect,color,12);}catch(InvalidOperationException){failed=true;}Check(failed,"Backend failure propagates to the existing renderer error boundary.");
            Console.WriteLine("Actual 1.7 drawing API: "+checks+" checks passed; fake backend, no GPU/game acceptance.");
        }
        finally{GuiBackend.Current=previous;Event.current=previousEvent;}
    }
    private sealed class Backend : IGuiBackend
    {
        internal int Fills,MeshCalls,Mode,GuiConversions;internal float Radius;internal Color FillColor;internal GuiVertex[] Mesh,RenderedMesh;internal Texture Texture;internal bool ScreenSpace,Fail;
        public bool MouseOutsideHitClip=>false;public int GroupCount=>0;public bool InlineControlsObscured=>false;
        public void FillRectRounded(Rect rect,Color color,float rounding){if(Fail)throw new InvalidOperationException("draw failure");Fills++;Radius=rounding;FillColor=color;}
        public void DrawPrimitives(int mode,ReadOnlySpan<GuiVertex> vertices,Texture texture,bool screenSpace){MeshCalls++;Mode=mode;Mesh=vertices.ToArray();RenderedMesh=vertices.ToArray();for(int i=0;i<RenderedMesh.Length;i++)if(!screenSpace)RenderedMesh[i].position=ToScreen(RenderedMesh[i].position);Texture=texture;ScreenSpace=screenSpace;}
        // Model the two distinct transform orders confirmed in real rev1248 IL.
        internal Vector2 ToScreen(Vector2 p)=>new Vector2((p.x+100)*2,(p.y+200)*1.5f);
        public Vector2 GuiToScreenPoint(Vector2 p){GuiConversions++;return new Vector2(p.x*2+100,p.y*1.5f+200);}public Vector2 ScreenToGuiPoint(Vector2 p)=>p;
        public Vector2 MeasureText(int fontIndex,float fontSize,FontStyle fontStyle,string text,float wrapWidth)=>default;
        public float LineSpacing(int fontIndex,float fontSize)=>0;public void FontMetrics(int fontIndex,out float importSize,out float ascent,out float lineSpacing){importSize=ascent=lineSpacing=0;}
        public int RegisterFont(string name,byte[] fontData,float importSize,int renderingMode)=>0;
        public int TextIndexAt(int fontIndex,float fontSize,FontStyle fontStyle,string text,float wrapWidth,Vector2 local)=>0;
        public Vector2 TextCursorPos(int fontIndex,float fontSize,FontStyle fontStyle,string text,float wrapWidth,int index,out float lineHeight){lineHeight=0;return default;}
        public void TextSelectionRects(int fontIndex,float fontSize,FontStyle fontStyle,string text,float wrapWidth,int from,int to,List<Rect> rects){}
        public void DrawText(Rect rect,string text,int fontIndex,float fontSize,FontStyle fontStyle,TextAnchor anchor,bool wordWrap,Color color,Rect clipRect){}
        public void DrawTexture(Rect rect,Texture image,ScaleMode scaleMode,Color color){}public void DrawTextureWithTexCoords(Rect rect,Texture image,Rect texCoords,Color color){}
        public void DrawTextureWithMaterial(Rect rect,Texture image,Rect texCoords,Color color,Material material,Waylith.Rendering.Material passMaterial){}
        public void FillRect(Rect rect,Color color){}public void DrawTextureQuad(Vector2 p0,Vector2 p1,Vector2 p2,Vector2 p3,Texture image,Color color){}
        public void SetMatrix(Matrix4x4 matrix){}public void PushGroup(Rect rect){}public void PushScrollView(Rect clipRect,Vector2 contentOffset){}public void PopGroup(){}
        public void GetGroup(int index,out Rect rect,out Vector2 contentOrigin){rect=default;contentOrigin=default;}
        public void BeginWindowContent(Rect windowRect){}public void EndWindowContent(){}public void PushClipRect(Rect rect){}public void PopClipRect(){}
        public void ActiveRenderTextureChanged(RenderTexture active){}public void SetTextLanguage(string languageFolderName){}
    }
}
