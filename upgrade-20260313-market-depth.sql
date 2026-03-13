USE tradingsystem;

DROP PROCEDURE IF EXISTS ApplyColumnIfMissing;
DROP PROCEDURE IF EXISTS ApplyIndexIfMissing;
DROP PROCEDURE IF EXISTS ApplyForeignKeyIfMissing;

DELIMITER $$
CREATE PROCEDURE ApplyColumnIfMissing(
    IN p_table_name VARCHAR(64),
    IN p_column_name VARCHAR(64),
    IN p_alter_sql TEXT
)
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = p_table_name
          AND COLUMN_NAME = p_column_name
    ) THEN
        SET @ddl = p_alter_sql;
        PREPARE stmt FROM @ddl;
        EXECUTE stmt;
        DEALLOCATE PREPARE stmt;
    END IF;
END $$

CREATE PROCEDURE ApplyIndexIfMissing(
    IN p_table_name VARCHAR(64),
    IN p_index_name VARCHAR(64),
    IN p_create_sql TEXT
)
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = p_table_name
          AND INDEX_NAME = p_index_name
    ) THEN
        SET @ddl = p_create_sql;
        PREPARE stmt FROM @ddl;
        EXECUTE stmt;
        DEALLOCATE PREPARE stmt;
    END IF;
END $$

CREATE PROCEDURE ApplyForeignKeyIfMissing(
    IN p_table_name VARCHAR(64),
    IN p_constraint_name VARCHAR(64),
    IN p_alter_sql TEXT
)
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = p_table_name
          AND CONSTRAINT_NAME = p_constraint_name
    ) THEN
        SET @ddl = p_alter_sql;
        PREPARE stmt FROM @ddl;
        EXECUTE stmt;
        DEALLOCATE PREPARE stmt;
    END IF;
END $$
DELIMITER ;

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
    PRIMARY KEY (TradeAccountId, TradeUserGroupId)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS TradeGroupPermissions (
    TradeUserGroupId INT NOT NULL,
    TradePermissionId INT NOT NULL,
    PRIMARY KEY (TradeUserGroupId, TradePermissionId)
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
    UNIQUE KEY UX_ScheduledTasks_Name (Name)
) ENGINE=InnoDB;

CALL ApplyColumnIfMissing(
    'TradeOrders',
    'TradeAccountId',
    'ALTER TABLE TradeOrders ADD COLUMN TradeAccountId BIGINT NULL AFTER Id'
);

CALL ApplyColumnIfMissing(
    'TradeOrders',
    'ExecutedVolume',
    'ALTER TABLE TradeOrders ADD COLUMN ExecutedVolume DECIMAL(18, 4) NOT NULL DEFAULT 0 AFTER ServerId'
);

CALL ApplyColumnIfMissing(
    'TradeOrders',
    'QueuedVolume',
    'ALTER TABLE TradeOrders ADD COLUMN QueuedVolume DECIMAL(18, 4) NOT NULL DEFAULT 0 AFTER ExecutedVolume'
);

CALL ApplyColumnIfMissing(
    'TradeOrders',
    'Status',
    'ALTER TABLE TradeOrders ADD COLUMN Status VARCHAR(50) NOT NULL DEFAULT ''Pending'' AFTER IsProcessed'
);

CALL ApplyColumnIfMissing(
    'TradeOrders',
    'CreatedAt',
    'ALTER TABLE TradeOrders ADD COLUMN CreatedAt TIMESTAMP(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) AFTER Status'
);

CALL ApplyColumnIfMissing(
    'TradeOrders',
    'ProcessedAt',
    'ALTER TABLE TradeOrders ADD COLUMN ProcessedAt TIMESTAMP(6) NULL AFTER CreatedAt'
);

CALL ApplyColumnIfMissing(
    'StockPrices',
    'AvailableVolume',
    'ALTER TABLE StockPrices ADD COLUMN AvailableVolume DECIMAL(18, 4) NOT NULL DEFAULT 0 AFTER TotalStockVolume'
);

CALL ApplyColumnIfMissing(
    'StockPrices',
    'PendingBuyVolume',
    'ALTER TABLE StockPrices ADD COLUMN PendingBuyVolume DECIMAL(18, 4) NOT NULL DEFAULT 0 AFTER SellVolume'
);

CALL ApplyColumnIfMissing(
    'StockPrices',
    'PendingSellVolume',
    'ALTER TABLE StockPrices ADD COLUMN PendingSellVolume DECIMAL(18, 4) NOT NULL DEFAULT 0 AFTER PendingBuyVolume'
);

CALL ApplyColumnIfMissing(
    'JobExecutionHistories',
    'ScheduledTaskId',
    'ALTER TABLE JobExecutionHistories ADD COLUMN ScheduledTaskId BIGINT NULL AFTER Id'
);

CALL ApplyColumnIfMissing(
    'JobExecutionHistories',
    'TaskName',
    'ALTER TABLE JobExecutionHistories ADD COLUMN TaskName VARCHAR(200) NULL AFTER ScheduledTaskId'
);

CALL ApplyColumnIfMissing(
    'JobExecutionHistories',
    'TaskType',
    'ALTER TABLE JobExecutionHistories ADD COLUMN TaskType VARCHAR(50) NULL AFTER TaskName'
);

CALL ApplyColumnIfMissing(
    'JobExecutionHistories',
    'Ticker',
    'ALTER TABLE JobExecutionHistories ADD COLUMN Ticker VARCHAR(10) NULL AFTER ServerId'
);

CALL ApplyColumnIfMissing(
    'JobExecutionHistories',
    'DurationMs',
    'ALTER TABLE JobExecutionHistories ADD COLUMN DurationMs DECIMAL(18, 4) NULL AFTER EndTime'
);

CALL ApplyColumnIfMissing(
    'JobExecutionHistories',
    'SchedulerInstance',
    'ALTER TABLE JobExecutionHistories ADD COLUMN SchedulerInstance VARCHAR(200) NULL AFTER DurationMs'
);

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

UPDATE StockPrices
SET AvailableVolume = TotalStockVolume
WHERE AvailableVolume = 0;

UPDATE TradeOrders
SET TradeAccountId = 1
WHERE TradeAccountId IS NULL;

SET @make_trade_account_required = IF(
    EXISTS (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'TradeOrders'
          AND COLUMN_NAME = 'TradeAccountId'
          AND IS_NULLABLE = 'YES'
    ),
    'ALTER TABLE TradeOrders MODIFY COLUMN TradeAccountId BIGINT NOT NULL',
    'SELECT 1'
);
PREPARE stmt FROM @make_trade_account_required;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

CALL ApplyIndexIfMissing(
    'TradeOrders',
    'IX_TradeOrders_TradeAccountId_CreatedAt',
    'CREATE INDEX IX_TradeOrders_TradeAccountId_CreatedAt ON TradeOrders (TradeAccountId, CreatedAt)'
);

CALL ApplyIndexIfMissing(
    'JobExecutionHistories',
    'IX_JobExecutionHistories_ScheduledTaskId_StartTime',
    'CREATE INDEX IX_JobExecutionHistories_ScheduledTaskId_StartTime ON JobExecutionHistories (ScheduledTaskId, StartTime)'
);

CALL ApplyIndexIfMissing(
    'ScheduledTasks',
    'IX_ScheduledTasks_TaskType_IsEnabled_IsPaused',
    'CREATE INDEX IX_ScheduledTasks_TaskType_IsEnabled_IsPaused ON ScheduledTasks (TaskType, IsEnabled, IsPaused)'
);

CALL ApplyIndexIfMissing(
    'ScheduledTasks',
    'IX_ScheduledTasks_ServerId_Ticker',
    'CREATE INDEX IX_ScheduledTasks_ServerId_Ticker ON ScheduledTasks (ServerId, Ticker)'
);

CALL ApplyIndexIfMissing(
    'ScheduledTasks',
    'IX_ScheduledTasks_NextFireTime',
    'CREATE INDEX IX_ScheduledTasks_NextFireTime ON ScheduledTasks (NextFireTime)'
);

CALL ApplyForeignKeyIfMissing(
    'TradeAccountGroups',
    'FK_TradeAccountGroups_TradeAccounts',
    'ALTER TABLE TradeAccountGroups ADD CONSTRAINT FK_TradeAccountGroups_TradeAccounts FOREIGN KEY (TradeAccountId) REFERENCES TradeAccounts(Id) ON DELETE CASCADE'
);

CALL ApplyForeignKeyIfMissing(
    'TradeAccountGroups',
    'FK_TradeAccountGroups_TradeUserGroups',
    'ALTER TABLE TradeAccountGroups ADD CONSTRAINT FK_TradeAccountGroups_TradeUserGroups FOREIGN KEY (TradeUserGroupId) REFERENCES TradeUserGroups(Id) ON DELETE CASCADE'
);

CALL ApplyForeignKeyIfMissing(
    'TradeGroupPermissions',
    'FK_TradeGroupPermissions_TradeUserGroups',
    'ALTER TABLE TradeGroupPermissions ADD CONSTRAINT FK_TradeGroupPermissions_TradeUserGroups FOREIGN KEY (TradeUserGroupId) REFERENCES TradeUserGroups(Id) ON DELETE CASCADE'
);

CALL ApplyForeignKeyIfMissing(
    'TradeGroupPermissions',
    'FK_TradeGroupPermissions_TradePermissions',
    'ALTER TABLE TradeGroupPermissions ADD CONSTRAINT FK_TradeGroupPermissions_TradePermissions FOREIGN KEY (TradePermissionId) REFERENCES TradePermissions(Id) ON DELETE CASCADE'
);

CALL ApplyForeignKeyIfMissing(
    'ScheduledTasks',
    'FK_ScheduledTasks_TradingServers',
    'ALTER TABLE ScheduledTasks ADD CONSTRAINT FK_ScheduledTasks_TradingServers FOREIGN KEY (ServerId) REFERENCES TradingServers(Id)'
);

CALL ApplyForeignKeyIfMissing(
    'TradeOrders',
    'FK_TradeOrders_TradeAccounts',
    'ALTER TABLE TradeOrders ADD CONSTRAINT FK_TradeOrders_TradeAccounts FOREIGN KEY (TradeAccountId) REFERENCES TradeAccounts(Id)'
);

CALL ApplyForeignKeyIfMissing(
    'JobExecutionHistories',
    'FK_JobExecutionHistories_ScheduledTasks',
    'ALTER TABLE JobExecutionHistories ADD CONSTRAINT FK_JobExecutionHistories_ScheduledTasks FOREIGN KEY (ScheduledTaskId) REFERENCES ScheduledTasks(Id) ON DELETE SET NULL'
);

DROP PROCEDURE IF EXISTS ApplyColumnIfMissing;
DROP PROCEDURE IF EXISTS ApplyIndexIfMissing;
DROP PROCEDURE IF EXISTS ApplyForeignKeyIfMissing;
