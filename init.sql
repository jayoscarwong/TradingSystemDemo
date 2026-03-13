USE tradingsystem;

CREATE TABLE IF NOT EXISTS TradingServers (
    Id INT NOT NULL PRIMARY KEY,
    ServerName VARCHAR(100) NOT NULL,
    IsEnabled BOOLEAN NOT NULL DEFAULT TRUE,
    LastPingAt TIMESTAMP(6) NULL,
    INDEX IX_TradingServers_IsEnabled (IsEnabled)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS StockPrices (
    Ticker VARCHAR(10) NOT NULL PRIMARY KEY,
    CurrentPrice DECIMAL(18, 4) NOT NULL,
    TotalStockVolume DECIMAL(18, 4) NOT NULL,
    AvailableVolume DECIMAL(18, 4) NOT NULL DEFAULT 0,
    BuyVolume DECIMAL(18, 4) NOT NULL DEFAULT 0,
    SellVolume DECIMAL(18, 4) NOT NULL DEFAULT 0,
    PendingBuyVolume DECIMAL(18, 4) NOT NULL DEFAULT 0,
    PendingSellVolume DECIMAL(18, 4) NOT NULL DEFAULT 0,
    LastUpdatedAt TIMESTAMP(6) DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    RowVersion TIMESTAMP(6) DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS TradeAccounts (
    Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    Name VARCHAR(200) NOT NULL,
    Username VARCHAR(100) NOT NULL,
    Email VARCHAR(200) NOT NULL,
    PasswordHash VARBINARY(64) NOT NULL,
    PasswordSalt VARBINARY(128) NOT NULL,
    IsDisabled BOOLEAN NOT NULL DEFAULT FALSE,
    CreatedAt TIMESTAMP(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    LastLoginAt TIMESTAMP(6) NULL,
    RowVersion TIMESTAMP(6) DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    UNIQUE KEY UX_TradeAccounts_Username (Username),
    UNIQUE KEY UX_TradeAccounts_Email (Email)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS TradeUserGroups (
    Id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    Name VARCHAR(100) NOT NULL,
    Description VARCHAR(300) NOT NULL,
    IsSystemGroup BOOLEAN NOT NULL DEFAULT TRUE,
    UNIQUE KEY UX_TradeUserGroups_Name (Name)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS TradePermissions (
    Id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    Code VARCHAR(100) NOT NULL,
    Description VARCHAR(300) NOT NULL,
    UNIQUE KEY UX_TradePermissions_Code (Code)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS TradeAccountGroups (
    TradeAccountId BIGINT NOT NULL,
    TradeUserGroupId INT NOT NULL,
    PRIMARY KEY (TradeAccountId, TradeUserGroupId),
    CONSTRAINT FK_TradeAccountGroups_TradeAccounts FOREIGN KEY (TradeAccountId) REFERENCES TradeAccounts(Id) ON DELETE CASCADE,
    CONSTRAINT FK_TradeAccountGroups_TradeUserGroups FOREIGN KEY (TradeUserGroupId) REFERENCES TradeUserGroups(Id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS TradeGroupPermissions (
    TradeUserGroupId INT NOT NULL,
    TradePermissionId INT NOT NULL,
    PRIMARY KEY (TradeUserGroupId, TradePermissionId),
    CONSTRAINT FK_TradeGroupPermissions_TradeUserGroups FOREIGN KEY (TradeUserGroupId) REFERENCES TradeUserGroups(Id) ON DELETE CASCADE,
    CONSTRAINT FK_TradeGroupPermissions_TradePermissions FOREIGN KEY (TradePermissionId) REFERENCES TradePermissions(Id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS ScheduledTasks (
    Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    Name VARCHAR(200) NOT NULL,
    Description VARCHAR(500) NULL,
    TaskType VARCHAR(50) NOT NULL,
    ScheduleType VARCHAR(20) NOT NULL,
    CronExpression VARCHAR(120) NULL,
    IntervalSeconds INT NULL,
    RepeatCount INT NULL,
    ServerId INT NULL,
    Ticker VARCHAR(10) NULL,
    IsSystemTask BOOLEAN NOT NULL DEFAULT FALSE,
    IsEnabled BOOLEAN NOT NULL DEFAULT TRUE,
    IsPaused BOOLEAN NOT NULL DEFAULT FALSE,
    AllowConcurrentExecution BOOLEAN NOT NULL DEFAULT FALSE,
    RuntimeStatus VARCHAR(50) NOT NULL DEFAULT 'Scheduled',
    CreatedAt TIMESTAMP(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    UpdatedAt TIMESTAMP(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    LastTriggeredAt TIMESTAMP(6) NULL,
    LastCompletedAt TIMESTAMP(6) NULL,
    CurrentExecutionStartedAt TIMESTAMP(6) NULL,
    NextFireTime TIMESTAMP(6) NULL,
    LastExecutionStatus VARCHAR(50) NULL,
    LastExecutionDurationMs DECIMAL(18, 4) NULL,
    AverageDurationMs DECIMAL(18, 4) NOT NULL DEFAULT 0,
    ExecutionCount BIGINT NOT NULL DEFAULT 0,
    FailureCount BIGINT NOT NULL DEFAULT 0,
    LastSchedulerInstance VARCHAR(200) NULL,
    LastError TEXT NULL,
    RowVersion TIMESTAMP(6) DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    UNIQUE KEY UX_ScheduledTasks_Name (Name),
    INDEX IX_ScheduledTasks_TaskType_IsEnabled_IsPaused (TaskType, IsEnabled, IsPaused),
    INDEX IX_ScheduledTasks_ServerId_Ticker (ServerId, Ticker),
    INDEX IX_ScheduledTasks_NextFireTime (NextFireTime),
    CONSTRAINT FK_ScheduledTasks_TradingServers FOREIGN KEY (ServerId) REFERENCES TradingServers(Id)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS TradeOrders (
    Id CHAR(36) NOT NULL PRIMARY KEY,
    TradeAccountId BIGINT NOT NULL,
    StockTicker VARCHAR(10) NOT NULL,
    BidAmount DECIMAL(18, 4) NOT NULL,
    Volume DECIMAL(18, 4) NOT NULL,
    IsBuy BOOLEAN NOT NULL,
    ServerId INT NOT NULL,
    ExecutedVolume DECIMAL(18, 4) NOT NULL DEFAULT 0,
    QueuedVolume DECIMAL(18, 4) NOT NULL DEFAULT 0,
    IsProcessed BOOLEAN NOT NULL DEFAULT FALSE,
    Status VARCHAR(50) NOT NULL DEFAULT 'Pending',
    CreatedAt TIMESTAMP(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    ProcessedAt TIMESTAMP(6) NULL,
    RowVersion TIMESTAMP(6) DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    INDEX IX_TradeOrders_Ticker_ServerId_CreatedAt (StockTicker, ServerId, CreatedAt),
    INDEX IX_TradeOrders_Status_IsProcessed (Status, IsProcessed),
    INDEX IX_TradeOrders_TradeAccountId_CreatedAt (TradeAccountId, CreatedAt),
    CONSTRAINT FK_TradeOrders_TradeAccounts FOREIGN KEY (TradeAccountId) REFERENCES TradeAccounts(Id),
    CONSTRAINT FK_TradeOrders_TradingServers FOREIGN KEY (ServerId) REFERENCES TradingServers(Id),
    CONSTRAINT FK_TradeOrders_StockPrices FOREIGN KEY (StockTicker) REFERENCES StockPrices(Ticker)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS JobExecutionHistories (
    Id CHAR(36) NOT NULL PRIMARY KEY,
    ScheduledTaskId BIGINT NULL,
    TaskName VARCHAR(200) NULL,
    TaskType VARCHAR(50) NULL,
    JobName VARCHAR(200) NOT NULL,
    ServerId INT NULL,
    Ticker VARCHAR(10) NULL,
    Status VARCHAR(50) NOT NULL,
    StartTime TIMESTAMP(6) NOT NULL,
    EndTime TIMESTAMP(6) NOT NULL,
    DurationMs DECIMAL(18, 4) NULL,
    SchedulerInstance VARCHAR(200) NULL,
    ErrorMessage TEXT NULL,
    INDEX IX_JobExecutionHistories_ScheduledTaskId_StartTime (ScheduledTaskId, StartTime),
    INDEX IX_JobExecutionHistories_JobName_StartTime (JobName, StartTime),
    CONSTRAINT FK_JobExecutionHistories_ScheduledTasks FOREIGN KEY (ScheduledTaskId) REFERENCES ScheduledTasks(Id) ON DELETE SET NULL
) ENGINE=InnoDB;

INSERT INTO TradingServers (Id, ServerName, IsEnabled)
VALUES
    (1, 'US-East Node', 1),
    (2, 'EU-West Node', 1),
    (3, 'AP-South Node', 1)
ON DUPLICATE KEY UPDATE
    ServerName = VALUES(ServerName),
    IsEnabled = VALUES(IsEnabled);

INSERT INTO StockPrices (Ticker, CurrentPrice, TotalStockVolume, AvailableVolume, BuyVolume, SellVolume, PendingBuyVolume, PendingSellVolume)
VALUES
    ('AMZN', 150.00, 2000.00, 2000.00, 0, 0, 0, 0),
    ('AAPL', 190.00, 3000.00, 3000.00, 0, 0, 0, 0),
    ('MSFT', 420.00, 2500.00, 2500.00, 0, 0, 0, 0),
    ('NVDA', 860.00, 1800.00, 1800.00, 0, 0, 0, 0)
ON DUPLICATE KEY UPDATE
    CurrentPrice = VALUES(CurrentPrice),
    TotalStockVolume = VALUES(TotalStockVolume),
    AvailableVolume = VALUES(AvailableVolume),
    BuyVolume = VALUES(BuyVolume),
    SellVolume = VALUES(SellVolume),
    PendingBuyVolume = VALUES(PendingBuyVolume),
    PendingSellVolume = VALUES(PendingSellVolume);

INSERT INTO TradePermissions (Id, Code, Description)
VALUES
    (1, 'accounts.manage', 'Create, update, disable, and delete trade accounts.'),
    (2, 'tasks.read', 'View task status, history, and monitoring data.'),
    (3, 'tasks.manage', 'Create, update, pause, resume, and delete scheduled tasks.'),
    (4, 'prices.read', 'Read real-time price snapshots.'),
    (5, 'trades.place', 'Place trade orders through the trading API.')
ON DUPLICATE KEY UPDATE
    Code = VALUES(Code),
    Description = VALUES(Description);

INSERT INTO TradeUserGroups (Id, Name, Description, IsSystemGroup)
VALUES
    (1, 'Administrators', 'Can manage accounts, tasks, and trading operations.', 1),
    (2, 'Traders', 'Can place trades and inspect live operational status.', 1),
    (3, 'Observers', 'Can view job status and live prices.', 1)
ON DUPLICATE KEY UPDATE
    Name = VALUES(Name),
    Description = VALUES(Description),
    IsSystemGroup = VALUES(IsSystemGroup);

INSERT INTO TradeGroupPermissions (TradeUserGroupId, TradePermissionId)
VALUES
    (1, 1),
    (1, 2),
    (1, 3),
    (1, 4),
    (1, 5),
    (2, 2),
    (2, 4),
    (2, 5),
    (3, 2),
    (3, 4)
ON DUPLICATE KEY UPDATE
    TradeUserGroupId = VALUES(TradeUserGroupId),
    TradePermissionId = VALUES(TradePermissionId);

INSERT INTO TradeAccounts (Id, Name, Username, Email, PasswordHash, PasswordSalt, IsDisabled, CreatedAt)
VALUES
    (
        1,
        'System Administrator',
        'admin',
        'admin@tradingsystem.local',
        0x173f5126e749782ddfdded16546788c60dc1facfed6578ac347befb6aeae70880c3e158c55679ccf98937d4221f27a58fecaccb6eeebecc359e9e0a26387a6e6,
        0xc15fa32a8c4665a1d7a45aa1fa81e2d34bb3c592ce75526b436453ba42e4edaf3ca6d9a97a502a885501511867bbf981f52b149b2e76bc53a08842c367c7f6643abf838f872cb201bd5e5748867a2516953938e18883a5d998e25af7760617592f22bc8c3c90cb102132a714bf417c1a253c6436203bed6eb7eb2609adadd6d3,
        0,
        CURRENT_TIMESTAMP(6)
    )
ON DUPLICATE KEY UPDATE
    Name = VALUES(Name),
    Email = VALUES(Email),
    IsDisabled = VALUES(IsDisabled);

INSERT INTO TradeAccountGroups (TradeAccountId, TradeUserGroupId)
VALUES
    (1, 1)
ON DUPLICATE KEY UPDATE
    TradeAccountId = VALUES(TradeAccountId),
    TradeUserGroupId = VALUES(TradeUserGroupId);

INSERT INTO ScheduledTasks (
    Id,
    Name,
    Description,
    TaskType,
    ScheduleType,
    CronExpression,
    IntervalSeconds,
    RepeatCount,
    ServerId,
    Ticker,
    IsSystemTask,
    IsEnabled,
    IsPaused,
    AllowConcurrentExecution,
    RuntimeStatus,
    CreatedAt,
    UpdatedAt,
    AverageDurationMs,
    ExecutionCount,
    FailureCount
)
VALUES
    (
        1,
        'Master Task Orchestrator',
        'System-owned parent task that reconciles server and ticker state into child polling tasks.',
        'MasterOrchestrator',
        'Cron',
        '0 0/5 * * * ?',
        NULL,
        NULL,
        NULL,
        NULL,
        1,
        1,
        0,
        0,
        'Scheduled',
        CURRENT_TIMESTAMP(6),
        CURRENT_TIMESTAMP(6),
        0,
        0,
        0
    )
ON DUPLICATE KEY UPDATE
    Description = VALUES(Description),
    TaskType = VALUES(TaskType),
    ScheduleType = VALUES(ScheduleType),
    CronExpression = VALUES(CronExpression),
    IsSystemTask = VALUES(IsSystemTask),
    IsEnabled = VALUES(IsEnabled),
    IsPaused = VALUES(IsPaused),
    AllowConcurrentExecution = VALUES(AllowConcurrentExecution),
    RuntimeStatus = VALUES(RuntimeStatus);

-- ====================================================================
-- QUARTZ.NET CLUSTERING TABLES (MySQL InnoDB)
-- ====================================================================
CREATE TABLE IF NOT EXISTS QRTZ_JOB_DETAILS(
    SCHED_NAME VARCHAR(120) NOT NULL,
    JOB_NAME VARCHAR(200) NOT NULL,
    JOB_GROUP VARCHAR(200) NOT NULL,
    DESCRIPTION VARCHAR(250) NULL,
    JOB_CLASS_NAME VARCHAR(250) NOT NULL,
    IS_DURABLE BOOLEAN NOT NULL,
    IS_NONCONCURRENT BOOLEAN NOT NULL,
    IS_UPDATE_DATA BOOLEAN NOT NULL,
    REQUESTS_RECOVERY BOOLEAN NOT NULL,
    JOB_DATA BLOB NULL,
    PRIMARY KEY (SCHED_NAME,JOB_NAME,JOB_GROUP)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS QRTZ_TRIGGERS (
    SCHED_NAME VARCHAR(120) NOT NULL,
    TRIGGER_NAME VARCHAR(200) NOT NULL,
    TRIGGER_GROUP VARCHAR(200) NOT NULL,
    JOB_NAME VARCHAR(200) NOT NULL,
    JOB_GROUP VARCHAR(200) NOT NULL,
    DESCRIPTION VARCHAR(250) NULL,
    NEXT_FIRE_TIME BIGINT(19) NULL,
    PREV_FIRE_TIME BIGINT(19) NULL,
    PRIORITY INTEGER NULL,
    TRIGGER_STATE VARCHAR(16) NOT NULL,
    TRIGGER_TYPE VARCHAR(8) NOT NULL,
    START_TIME BIGINT(19) NOT NULL,
    END_TIME BIGINT(19) NULL,
    CALENDAR_NAME VARCHAR(200) NULL,
    MISFIRE_INSTR SMALLINT(2) NULL,
    JOB_DATA BLOB NULL,
    PRIMARY KEY (SCHED_NAME,TRIGGER_NAME,TRIGGER_GROUP),
    FOREIGN KEY (SCHED_NAME,JOB_NAME,JOB_GROUP)
        REFERENCES QRTZ_JOB_DETAILS(SCHED_NAME,JOB_NAME,JOB_GROUP)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS QRTZ_SIMPLE_TRIGGERS (
    SCHED_NAME VARCHAR(120) NOT NULL,
    TRIGGER_NAME VARCHAR(200) NOT NULL,
    TRIGGER_GROUP VARCHAR(200) NOT NULL,
    REPEAT_COUNT BIGINT(7) NOT NULL,
    REPEAT_INTERVAL BIGINT(12) NOT NULL,
    TIMES_TRIGGERED BIGINT(10) NOT NULL,
    PRIMARY KEY (SCHED_NAME,TRIGGER_NAME,TRIGGER_GROUP),
    FOREIGN KEY (SCHED_NAME,TRIGGER_NAME,TRIGGER_GROUP)
        REFERENCES QRTZ_TRIGGERS(SCHED_NAME,TRIGGER_NAME,TRIGGER_GROUP)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS QRTZ_CRON_TRIGGERS (
    SCHED_NAME VARCHAR(120) NOT NULL,
    TRIGGER_NAME VARCHAR(200) NOT NULL,
    TRIGGER_GROUP VARCHAR(200) NOT NULL,
    CRON_EXPRESSION VARCHAR(120) NOT NULL,
    TIME_ZONE_ID VARCHAR(80),
    PRIMARY KEY (SCHED_NAME,TRIGGER_NAME,TRIGGER_GROUP),
    FOREIGN KEY (SCHED_NAME,TRIGGER_NAME,TRIGGER_GROUP)
        REFERENCES QRTZ_TRIGGERS(SCHED_NAME,TRIGGER_NAME,TRIGGER_GROUP)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS QRTZ_SIMPROP_TRIGGERS (
    SCHED_NAME VARCHAR(120) NOT NULL,
    TRIGGER_NAME VARCHAR(200) NOT NULL,
    TRIGGER_GROUP VARCHAR(200) NOT NULL,
    STR_PROP_1 VARCHAR(512) NULL,
    STR_PROP_2 VARCHAR(512) NULL,
    STR_PROP_3 VARCHAR(512) NULL,
    INT_PROP_1 INT NULL,
    INT_PROP_2 INT NULL,
    LONG_PROP_1 BIGINT NULL,
    LONG_PROP_2 BIGINT NULL,
    DEC_PROP_1 NUMERIC(13,4) NULL,
    DEC_PROP_2 NUMERIC(13,4) NULL,
    BOOL_PROP_1 BOOLEAN NULL,
    BOOL_PROP_2 BOOLEAN NULL,
    PRIMARY KEY (SCHED_NAME,TRIGGER_NAME,TRIGGER_GROUP),
    FOREIGN KEY (SCHED_NAME,TRIGGER_NAME,TRIGGER_GROUP)
        REFERENCES QRTZ_TRIGGERS(SCHED_NAME,TRIGGER_NAME,TRIGGER_GROUP)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS QRTZ_BLOB_TRIGGERS (
    SCHED_NAME VARCHAR(120) NOT NULL,
    TRIGGER_NAME VARCHAR(200) NOT NULL,
    TRIGGER_GROUP VARCHAR(200) NOT NULL,
    BLOB_DATA BLOB NULL,
    PRIMARY KEY (SCHED_NAME,TRIGGER_NAME,TRIGGER_GROUP),
    FOREIGN KEY (SCHED_NAME,TRIGGER_NAME,TRIGGER_GROUP)
        REFERENCES QRTZ_TRIGGERS(SCHED_NAME,TRIGGER_NAME,TRIGGER_GROUP)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS QRTZ_CALENDARS (
    SCHED_NAME VARCHAR(120) NOT NULL,
    CALENDAR_NAME VARCHAR(200) NOT NULL,
    CALENDAR BLOB NOT NULL,
    PRIMARY KEY (SCHED_NAME,CALENDAR_NAME)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS QRTZ_PAUSED_TRIGGER_GRPS (
    SCHED_NAME VARCHAR(120) NOT NULL,
    TRIGGER_GROUP VARCHAR(200) NOT NULL,
    PRIMARY KEY (SCHED_NAME,TRIGGER_GROUP)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS QRTZ_FIRED_TRIGGERS (
    SCHED_NAME VARCHAR(120) NOT NULL,
    ENTRY_ID VARCHAR(140) NOT NULL,
    TRIGGER_NAME VARCHAR(200) NOT NULL,
    TRIGGER_GROUP VARCHAR(200) NOT NULL,
    INSTANCE_NAME VARCHAR(200) NOT NULL,
    FIRED_TIME BIGINT(19) NOT NULL,
    SCHED_TIME BIGINT(19) NOT NULL,
    PRIORITY INTEGER NOT NULL,
    STATE VARCHAR(16) NOT NULL,
    JOB_NAME VARCHAR(200) NULL,
    JOB_GROUP VARCHAR(200) NULL,
    IS_NONCONCURRENT BOOLEAN NULL,
    REQUESTS_RECOVERY BOOLEAN NULL,
    PRIMARY KEY (SCHED_NAME,ENTRY_ID)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS QRTZ_SCHEDULER_STATE (
    SCHED_NAME VARCHAR(120) NOT NULL,
    INSTANCE_NAME VARCHAR(200) NOT NULL,
    LAST_CHECKIN_TIME BIGINT(19) NOT NULL,
    CHECKIN_INTERVAL BIGINT(19) NOT NULL,
    PRIMARY KEY (SCHED_NAME,INSTANCE_NAME)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS QRTZ_LOCKS (
    SCHED_NAME VARCHAR(120) NOT NULL,
    LOCK_NAME VARCHAR(40) NOT NULL,
    PRIMARY KEY (SCHED_NAME,LOCK_NAME)
) ENGINE=InnoDB;
