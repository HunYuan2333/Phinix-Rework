using System;
using System.Collections.Generic;
using System.Linq;
using PhinixClient;

public static partial class Program
{
    private static void SettingsBoundaries()
    {
        var facts=new NicknameFacts("old","old","test",true);
        var failures=new List<Tuple<string,Exception>>();var fault=ConnectionFault();
        int sends=0;
        var failedSave=new NicknameEditState(()=>facts,name=>System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fault).Throw(),name=>{sends++;return true;},(operation,error)=>failures.Add(Tuple.Create(operation,error)));
        failedSave.Change("new");failedSave.Save();
        Check(failedSave.Message=="saveFailed" && !failedSave.Committed && sends==0 && failures.Any(r=>r.Item1=="NicknameSaveFailed" && ReferenceEquals(r.Item2,fault)),"nickname settings failure retains cause and never dispatches a request");
        var unknown=new NicknameEditState(()=>facts,name=>{},name=>{sends++;System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fault).Throw();return false;},(operation,error)=>failures.Add(Tuple.Create(operation,error)));
        unknown.Change("next");unknown.Save();unknown.Save();
        Check(unknown.Committed && unknown.Message=="sendUnknown" && sends==1 && failures.Any(r=>r.Item1=="NicknameSendOutcomeUnknown" && ReferenceEquals(r.Item2,fault)),"nickname transport failure stays unknown with original cause and no automatic second send");
        var saved=new NicknameEditState(()=>facts,name=>{},name=>false,(operation,error)=>failures.Add(Tuple.Create(operation,error)));
        saved.Change("offline");saved.Save();
        Check(saved.Committed && saved.Message=="savedNotSent" && sends==1,"nickname local saved state remains distinct from network dispatch");
        int captures=0;var secondary=new ApplicationException("secondary");
        var refresh=new NicknameEditState(()=>{if(++captures>2)throw secondary;return facts;},name=>System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fault).Throw(),name=>true,(operation,error)=>failures.Add(Tuple.Create(operation,error)));
        refresh.Change("retry");refresh.Save();
        Check(refresh.Message=="saveFailed" && failures.Any(r=>r.Item1=="NicknameRefreshFailed" && ReferenceEquals(r.Item2,secondary)),"nickname refresh failure cannot replace original save failure or friendly status");
    }
}
