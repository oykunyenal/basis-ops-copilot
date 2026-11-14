namespace FakeSap.Simulation;

// Static catalogs the simulator draws from. All names are fictional.
internal static class SeedData
{
    public static readonly JobDefinition[] Jobs =
    [
        new("Z_BILLING_DAILY", 24, "BATCH_ADM"),
        new("Z_BILLING_RUN_HOURLY", 1, "BATCH_ADM"),
        new("SAP_COLLECTOR_FOR_PERFMONITOR", 1, "DDIC"),
        new("SAP_REORG_JOBS", 24, "DDIC"),
        new("SAP_REORG_SPOOL", 24, "DDIC"),
        new("SAP_CCMS_MONI_BATCH_DP", 1, "DDIC"),
        new("Z_MM_STOCK_SYNC", 2, "BATCH_ADM"),
        new("Z_FI_POSTING", 4, "BATCH_ADM"),
        new("Z_HR_PAYROLL_EXPORT", 12, "BATCH_HR"),
    ];

    public static readonly DumpTemplate[] Dumps =
    [
        new("TIME_OUT", "Z_SALES_REPORT", "Time limit exceeded.",
            "The program \"Z_SALES_REPORT\" exceeded the maximum runtime (rdisp/max_wprun_time). A SELECT on VBAP without a selective WHERE clause was running."),
        new("DBSQL_DUPLICATE_KEY_ERROR", "SAPLZ_BILLING", "The ABAP/4 Open SQL array insert results in duplicate database records.",
            "An INSERT into table ZBILL_LOG failed because a record with the same primary key already exists. The billing run may have been started twice."),
        new("MESSAGE_TYPE_X", "SAPLMBWL", "The current application triggered a termination with a short dump.",
            "The application raised message type X (M7 001). This usually indicates an inconsistent document state detected during goods movement posting."),
        new("TSV_TNEW_PAGE_ALLOC_FAILED", "Z_MM_STOCK_SYNC", "No more storage space available for extending an internal table.",
            "The internal table IT_STOCK could not be extended. The process reached the extended memory limit (ztta/roll_extension)."),
        new("CONVT_NO_NUMBER", "Z_FI_UPLOAD", "Unable to interpret \"12,5O\" as a number.",
            "A character field containing \"12,5O\" was assigned to a numeric field. The upload file most likely contains a typo (letter O instead of digit 0)."),
        new("GETWA_NOT_ASSIGNED", "SAPLZ_HR_EXPORT", "Field symbol has not yet been assigned.",
            "The field symbol <FS_EMPLOYEE> was accessed before an ASSIGN statement succeeded. The employee record for the current period was not found."),
    ];

    public static readonly (string Name, char Type, string Group)[] Users =
    [
        ("MEHMET", 'A', "SD"),
        ("AYSE", 'A', "FI"),
        ("CAN", 'A', "MM"),
        ("ELIF", 'A', "HR"),
        ("JDOE", 'A', "FI"),
        ("ASMITH", 'A', "SD"),
        ("MBROWN", 'A', "MM"),
        ("KTANAKA", 'A', "BASIS"),
        ("BATCH_ADM", 'B', "BASIS"),
        ("BATCH_HR", 'B', "HR"),
        ("RFC_CRM", 'B', "INTERFACE"),
    ];

    public static readonly string[] Developers = ["KTANAKA", "AYSE", "JDOE", "MBROWN"];

    public static readonly string[] TransportTexts =
    [
        "SD: pricing condition fix",
        "FI: new G/L account mapping",
        "MM: stock sync performance",
        "HR: payroll export field",
        "BC: RFC destination update",
        "SD: billing output form",
        "FI: tax code 2027",
    ];
}
