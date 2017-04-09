#include "process_routines.h"
#include "debug.h"
#include "global_data.h"
#include "process_list.h"
#include "ice_app_ctrl_scan.h"

VOID
IceStartProcessCallback(
    _Inout_     PEPROCESS                   PProcess,
    _In_        HANDLE                      HProcessId,
    _Inout_opt_ PPS_CREATE_NOTIFY_INFO      PCreateInfo
)
{
    NTSTATUS                                ntStatus;
    NTSTATUS                                ntScanResult;

    ntStatus                                = STATUS_SUCCESS;
    ntScanResult                            = STATUS_SUCCESS;

    LogInfo("Process %wZ (%d) starting", PCreateInfo->ImageFileName, (ULONG)(ULONG_PTR) HProcessId);

    if (gPData->IceSettings.BtEnableAppCtrlScan == 0)
    {
        LogInfo("AppCtrl scanning is disabled.");
        return;
    }

    __try
    {
        LogInfo("Sending scan request to user mode for process: %wZ (%d)", PCreateInfo->ImageFileName, (ULONG)(ULONG_PTR) HProcessId);
        ntStatus = IceAppCtrlScanProcess(PProcess, HProcessId, PCreateInfo, &ntScanResult);
        if (!NT_SUCCESS(ntStatus))
        {
            LogErrorNt(ntStatus, "IceAppCtrlScanProcess(%wZ, %d) failed. Result will not be added to cache", PCreateInfo->ImageFileName, (ULONG) (ULONG_PTR) HProcessId);
            ntScanResult = STATUS_SUCCESS;
            __leave;
        }

        // add to cache

    }
    __finally
    {

    }

    if (ntScanResult == 5) ntScanResult = STATUS_ACCESS_DENIED;
    LogInfo("[AppCtrl] Process %wZ (%d) scan result: %d (%s)", PCreateInfo->ImageFileName, (ULONG)(ULONG_PTR) HProcessId, ntScanResult, ntScanResult ? "DENY" : "ALLOW");
    PCreateInfo->CreationStatus = ntScanResult;
}

VOID 
IceStopProcessCallback(
    _Inout_     PEPROCESS                   PProcess,
    _In_        HANDLE                      HProcessId
    )
{
    PProcess; HProcessId;
    LogInfo("Process %d stoping", (ULONG)(ULONG_PTR) HProcessId);
}

// https://msdn.microsoft.com/en-us/library/ff542860(v=vs.85).aspx
VOID
IceCreateProcessCallback(
    _Inout_     PEPROCESS                   PProcess,
    _In_        HANDLE                      HProcessId,
    _Inout_opt_ PPS_CREATE_NOTIFY_INFO      PCreateInfo
    )
{
    if (gPData->BUnloading)
    {
        LogInfo("Process %s (%d) while driver is unloading", (NULL != PCreateInfo) ? "STARTING" : "STOPPING", (ULONG)(ULONG_PTR) HProcessId);
        return;
    }

    // procesul se opreste
    if (NULL == PCreateInfo)
    {
        IceStopProcessCallback(PProcess, HProcessId);
        return;
    }

    IceStartProcessCallback(PProcess, HProcessId, PCreateInfo);
}

_Use_decl_anno_impl_
NTSTATUS
IceRegisterProcessCallback(
    VOID
    )
{
    NTSTATUS ntStatus = STATUS_SUCCESS;

    ntStatus = IceProLstInitialize((WORD) gPData->IceSettings.UlMaximumProcessCache);
    if (!NT_SUCCESS(ntStatus))
    {
        IceProLstUninitialize();
        LogErrorNt(ntStatus, "IceProLstInitialize");
        return ntStatus;
    }

    ntStatus = PsSetCreateProcessNotifyRoutineEx(IceCreateProcessCallback, FALSE);
    if (!NT_SUCCESS(ntStatus))
    {
        LogErrorNt(ntStatus, "PsSetCreateProcessNotifyRoutineEx - Failed to set process start callback");
    }
    gPData->BProcessCallbackSet = NT_SUCCESS(ntStatus);

    return ntStatus;
}

VOID
IceCleanupProcessCalback(
    VOID
)
{
    NTSTATUS ntStatus = STATUS_SUCCESS;

    if (gPData->BProcessCallbackSet)
    {
        ntStatus = PsSetCreateProcessNotifyRoutineEx(IceCreateProcessCallback, TRUE);
        if (!NT_SUCCESS(ntStatus))
        {
            LogWarningNt(ntStatus, "PsSetCreateProcessNotifyRoutineEx - Failed to Remove process start callback");
        }
        else
        {
            gPData->BProcessCallbackSet = FALSE;
        }
    }

    IceProLstUninitialize();
}