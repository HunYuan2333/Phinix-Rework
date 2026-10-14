using System;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Verse;

internal static partial class Program
{
    private static void CheckImageLeases()
    {
        Reject(()=>ClientTextureImage.FromSource(()=>null,null,null,default(ClientPresentationImageSize)),"Dynamic image producers must provide an owner lifetime guard");
        int calls=0;bool active=true;long revision=1;var texture=new Texture();
        var source=ClientTextureImage.FromSource(()=>{calls++;return texture;},()=>active,()=>revision,new ClientPresentationImageSize(64,64));
        Check(source.Size.Width==64&&source.Revision==1&&calls==0,"Metadata never resolves or reads GPU data");
        var lease=(IClientTextureImageLease)source.Acquire();
        Check(calls==1&&lease.TryGetTexture(out var read)&&ReferenceEquals(read,texture),"Drawing lease borrows the exact base Texture");
        revision++;Check(!lease.IsValid&&!lease.TryGetTexture(out read),"Revision changes revoke an existing lease");
        lease.Dispose();Check(!lease.IsValid,"Disposed leases cannot expose textures");
        using(var next=source.Acquire())Check(next.IsValid,"A new frame can borrow the current revision");
        active=false;int before=calls;Check(source.Acquire()==null&&calls==before,"Inactive owner is not resolved");
        active=true;lease=(IClientTextureImageLease)source.Acquire();source.Invalidate();
        Check(!lease.IsValid&&source.Acquire()==null,"Explicit source invalidation revokes old and future leases");lease.Dispose();
        ClientTextureImage reentrant=null;
        reentrant=ClientTextureImage.FromSource(()=>{reentrant.Invalidate();return texture;},()=>true,null,default(ClientPresentationImageSize));
        Check(reentrant.Acquire()==null,"Invalidation during resolution cannot produce a stale lease");
        var changed=ClientTextureImage.FromSource(()=>{revision++;return texture;},()=>true,()=>revision,default(ClientPresentationImageSize));
        Check(changed.Acquire()==null,"Revision change during resolution cannot escape the bridge");
        var shared=ClientTextureImage.FromTexture(texture);
        using(var one=shared.Acquire())using(var two=shared.Acquire())
        {one.Dispose();Check(two.IsValid,"Releasing one drawing lease leaves other views and owner resources intact");}
        using(var later=shared.Acquire())Check(later.IsValid,"Closing drawing leases does not invalidate a shared source");

        int visible=0,hidden=0,errors=0;
        var first=ClientTextureImage.FromSource(()=>{visible++;return texture;},()=>true,null,default(ClientPresentationImageSize));
        var offscreen=ClientTextureImage.FromSource(()=>{hidden++;return texture;},()=>true,null,default(ClientPresentationImageSize));
        var throwing=ClientTextureImage.FromSource(()=>{throw new InvalidOperationException("image");},()=>true,null,default(ClientPresentationImageSize));
        var rows=new[]{new ClientPresentationRow("first","First").WithImage(first),new ClientPresentationRow("throw","Throw").WithImage(throwing),new ClientPresentationRow("hidden","Hidden").WithImage(offscreen)};
        var service=new ClientPresentationService(new Lifetime(),new Theme(),()=>true,_=>errors++);
        using(var session=service.Open(new Module(),"images"))
        {
            session.Publish(new ClientPresentationPage("images","",1,new[]{new ClientPresentationSection("images","","",list:new ClientPresentationList("images",rows,"").WithReadOnly())}),null,()=>true);
            Event.current.type=EventType.Layout;session.Draw(new ClientPresentationBounds(0,0,600,60));
            Event.current.type=EventType.MouseDown;session.Draw(new ClientPresentationBounds(0,0,600,60));
            Check(visible==0&&hidden==0,"Layout and input never resolve either visible or offscreen images");
            Event.current.type=EventType.Repaint;session.Draw(new ClientPresentationBounds(0,0,600,60));
            Check(visible==1&&hidden==0,"Overscan rows outside the viewport never acquire images");
            Check(session.FailureCode==null,"Passive image errors do not invalidate business actions");
            session.Draw(new ClientPresentationBounds(0,0,600,400));session.Draw(new ClientPresentationBounds(0,0,600,400));
            Check(errors==1&&session.FailureCode==null,"Throwing images fall back to labels and log once per session");
        }
        Event.current.type=EventType.Layout;
    }
}
