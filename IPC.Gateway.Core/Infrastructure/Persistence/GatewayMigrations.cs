/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Infrastructure.Persistence
* 项目描述 ：
* 类 名 称 ：GatewayMigrations
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Core.Infrastructure.Persistence
* 机器名称 ：UNKNOWN 
* CLR 版本 ：10.0.0
* 作    者 ：ipc
* 创建时间 ：2026-06-23 17:52:06
* 更新时间 ：2026-06-23 17:52:06
* 版 本 号 ：v1.0.0.0
*******************************************************************
* Copyright @ ipc 2026. All rights reserved.
*******************************************************************
//----------------------------------------------------------------*/
using SqlSugar;

namespace IPC.Gateway.Core.Infrastructure.Persistence;

public static class GatewayMigrations
{
    public static IReadOnlyList<DatabaseMigration> All { get; } = new List<DatabaseMigration>
    {
        new DatabaseMigration(
            "202606170001_initial_gateway_schema",
            "Create gateway configuration, user and schema migration tables.",
            PostgreSqlInitialSchema,
            MySqlInitialSchema,
            SqlServerInitialSchema,
            SqliteInitialSchema),
        new DatabaseMigration(
            "202606180001_runtime_state_cache",
            "Create runtime state cache tables.",
            PostgreSqlRuntimeStateSchema,
            MySqlRuntimeStateSchema,
            SqlServerRuntimeStateSchema,
            SqliteRuntimeStateSchema),
        new DatabaseMigration(
            "202606190001_runtime_device_reconnect_delay",
            "Add reconnect delay to persisted runtime device statuses.",
            PostgreSqlReconnectDelaySchema,
            MySqlReconnectDelaySchema,
            SqlServerReconnectDelaySchema,
            SqliteReconnectDelaySchema),
        new DatabaseMigration(
            "202606200001_audit_log_store",
            "Create gateway audit log table.",
            PostgreSqlAuditLogSchema,
            MySqlAuditLogSchema,
            SqlServerAuditLogSchema,
            SqliteAuditLogSchema),
        new DatabaseMigration(
            "202606210001_runtime_tag_cleaning_state",
            "Add data cleaning state to persisted runtime tag snapshots.",
            PostgreSqlRuntimeTagCleaningStateSchema,
            MySqlRuntimeTagCleaningStateSchema,
            SqlServerRuntimeTagCleaningStateSchema,
            SqliteRuntimeTagCleaningStateSchema),
        new DatabaseMigration(
            "202606220001_role_management",
            "Create gateway role and permission tables.",
            PostgreSqlRoleManagementSchema,
            MySqlRoleManagementSchema,
            SqlServerRoleManagementSchema,
            SqliteRoleManagementSchema),
        new DatabaseMigration(
            "202606220002_account_security_state",
            "Add account security state for password changes and login lockout.",
            PostgreSqlAccountSecurityStateSchema,
            MySqlAccountSecurityStateSchema,
            SqlServerAccountSecurityStateSchema,
            SqliteAccountSecurityStateSchema),
        new DatabaseMigration(
            "202607150001_runtime_channel_identity",
            "Add channel and stable object identities to persisted runtime state.",
            PostgreSqlRuntimeChannelIdentitySchema,
            MySqlRuntimeChannelIdentitySchema,
            SqlServerRuntimeChannelIdentitySchema,
            SqliteRuntimeChannelIdentitySchema)
    };

    private const string PostgreSqlInitialSchema = @"
create table if not exists gateway_configurations (
    id text primary key,
    config_type text not null,
    version integer not null,
    payload text not null,
    active boolean not null,
    created_utc timestamp without time zone not null,
    source text null,
    description text null
);

create unique index if not exists uq_gateway_configurations_type_version
    on gateway_configurations(config_type, version);

create index if not exists ix_gateway_configurations_type_active
    on gateway_configurations(config_type, active);

create index if not exists ix_gateway_configurations_type_version
    on gateway_configurations(config_type, version desc);

create table if not exists gateway_users (
    id text primary key,
    username text not null unique,
    display_name text null,
    role text not null,
    enabled boolean not null,
    password_hash text not null,
    password_salt text not null,
    created_utc timestamp without time zone not null
);

create index if not exists ix_gateway_users_username
    on gateway_users(lower(username));";

    private const string MySqlInitialSchema = @"
create table if not exists gateway_configurations (
    id varchar(64) not null primary key,
    config_type varchar(32) not null,
    version int not null,
    payload longtext not null,
    active bit not null,
    created_utc datetime not null,
    source varchar(128) null,
    description varchar(512) null,
    unique key uq_gateway_configurations_type_version(config_type, version),
    key ix_gateway_configurations_type_active(config_type, active),
    key ix_gateway_configurations_type_version(config_type, version)
) character set utf8mb4 collate utf8mb4_unicode_ci;

create table if not exists gateway_users (
    id varchar(64) not null primary key,
    username varchar(64) not null unique,
    display_name varchar(128) null,
    role varchar(32) not null,
    enabled bit not null,
    password_hash varchar(256) not null,
    password_salt varchar(128) not null,
    created_utc datetime not null,
    key ix_gateway_users_username(username)
) character set utf8mb4 collate utf8mb4_unicode_ci;";

    private const string SqlServerInitialSchema = @"
if object_id(N'gateway_configurations', N'U') is null
begin
    create table gateway_configurations (
        id nvarchar(64) not null primary key,
        config_type nvarchar(32) not null,
        version int not null,
        payload nvarchar(max) not null,
        active bit not null,
        created_utc datetime2 not null,
        source nvarchar(128) null,
        description nvarchar(512) null
    );
end;

if not exists (select 1 from sys.indexes where name = N'uq_gateway_configurations_type_version' and object_id = object_id(N'gateway_configurations'))
    create unique index uq_gateway_configurations_type_version on gateway_configurations(config_type, version);

if not exists (select 1 from sys.indexes where name = N'ix_gateway_configurations_type_active' and object_id = object_id(N'gateway_configurations'))
    create index ix_gateway_configurations_type_active on gateway_configurations(config_type, active);

if not exists (select 1 from sys.indexes where name = N'ix_gateway_configurations_type_version' and object_id = object_id(N'gateway_configurations'))
    create index ix_gateway_configurations_type_version on gateway_configurations(config_type, version desc);

if object_id(N'gateway_users', N'U') is null
begin
    create table gateway_users (
        id nvarchar(64) not null primary key,
        username nvarchar(64) not null unique,
        display_name nvarchar(128) null,
        role nvarchar(32) not null,
        enabled bit not null,
        password_hash nvarchar(256) not null,
        password_salt nvarchar(128) not null,
        created_utc datetime2 not null
    );
end;

if not exists (select 1 from sys.indexes where name = N'ix_gateway_users_username' and object_id = object_id(N'gateway_users'))
    create index ix_gateway_users_username on gateway_users(username);";

    private const string SqliteInitialSchema = @"
create table if not exists gateway_configurations (
    id text not null primary key,
    config_type text not null,
    version integer not null,
    payload text not null,
    active integer not null,
    created_utc text not null,
    source text null,
    description text null
);

create unique index if not exists uq_gateway_configurations_type_version
    on gateway_configurations(config_type, version);

create index if not exists ix_gateway_configurations_type_active
    on gateway_configurations(config_type, active);

create index if not exists ix_gateway_configurations_type_version
    on gateway_configurations(config_type, version desc);

create table if not exists gateway_users (
    id text not null primary key,
    username text not null unique,
    display_name text null,
    role text not null,
    enabled integer not null,
    password_hash text not null,
    password_salt text not null,
    created_utc text not null
);

create index if not exists ix_gateway_users_username
    on gateway_users(username);";

    private const string PostgreSqlRuntimeStateSchema = @"
create table if not exists gateway_runtime_device_statuses (
    id text primary key,
    project_id text not null,
    device_id text not null,
    device_name text not null,
    protocol text not null,
    enabled boolean not null,
    is_connected boolean not null,
    status text not null,
    consecutive_failures integer not null,
    total_reads bigint not null,
    successful_reads bigint not null,
    failed_reads bigint not null,
    success_rate double precision not null,
    last_poll_utc timestamp without time zone not null,
    last_success_utc timestamp without time zone not null,
    last_failure_utc timestamp without time zone not null,
    last_error text null,
    updated_utc timestamp without time zone not null
);

create index if not exists ix_gateway_runtime_device_statuses_project
    on gateway_runtime_device_statuses(project_id);

create table if not exists gateway_runtime_tag_snapshots (
    id text primary key,
    project_id text not null,
    device_id text not null,
    device_name text not null,
    group_id text null,
    group_name text null,
    tag_id text not null,
    tag_name text not null,
    data_type text not null,
    raw_value_text text null,
    value_text text null,
    unit text null,
    point_code text null,
    source text null,
    quality text not null,
    cleaning_applied boolean not null default false,
    cleaning_action text null,
    cleaning_message text null,
    timestamp_utc timestamp without time zone not null,
    error_message text null,
    updated_utc timestamp without time zone not null
);

create index if not exists ix_gateway_runtime_tag_snapshots_project
    on gateway_runtime_tag_snapshots(project_id);

create index if not exists ix_gateway_runtime_tag_snapshots_tag
    on gateway_runtime_tag_snapshots(project_id, tag_id);

create table if not exists gateway_runtime_errors (
    id text primary key,
    project_id text not null,
    category text not null,
    device_name text null,
    group_name text null,
    tag_name text null,
    message text null,
    suggestion text null,
    source text null,
    timestamp_utc timestamp without time zone not null,
    updated_utc timestamp without time zone not null
);

create index if not exists ix_gateway_runtime_errors_project_time
    on gateway_runtime_errors(project_id, timestamp_utc desc);";

    private const string MySqlRuntimeStateSchema = @"
create table if not exists gateway_runtime_device_statuses (
    id varchar(64) not null primary key,
    project_id varchar(64) not null,
    device_id varchar(64) not null,
    device_name varchar(128) not null,
    protocol varchar(64) not null,
    enabled bit not null,
    is_connected bit not null,
    status varchar(32) not null,
    consecutive_failures int not null,
    total_reads bigint not null,
    successful_reads bigint not null,
    failed_reads bigint not null,
    success_rate double not null,
    last_poll_utc datetime not null,
    last_success_utc datetime not null,
    last_failure_utc datetime not null,
    last_error longtext null,
    updated_utc datetime not null,
    key ix_gateway_runtime_device_statuses_project(project_id)
) character set utf8mb4 collate utf8mb4_unicode_ci;

create table if not exists gateway_runtime_tag_snapshots (
    id varchar(64) not null primary key,
    project_id varchar(64) not null,
    device_id varchar(64) not null,
    device_name varchar(128) not null,
    group_id varchar(64) null,
    group_name varchar(128) null,
    tag_id varchar(64) not null,
    tag_name varchar(128) not null,
    data_type varchar(64) not null,
    raw_value_text longtext null,
    value_text longtext null,
    unit varchar(64) null,
    point_code varchar(256) null,
    source varchar(128) null,
    quality varchar(32) not null,
    cleaning_applied bit not null default 0,
    cleaning_action varchar(64) null,
    cleaning_message longtext null,
    timestamp_utc datetime not null,
    error_message longtext null,
    updated_utc datetime not null,
    key ix_gateway_runtime_tag_snapshots_project(project_id),
    key ix_gateway_runtime_tag_snapshots_tag(project_id, tag_id)
) character set utf8mb4 collate utf8mb4_unicode_ci;

create table if not exists gateway_runtime_errors (
    id varchar(64) not null primary key,
    project_id varchar(64) not null,
    category varchar(64) not null,
    device_name varchar(128) null,
    group_name varchar(128) null,
    tag_name varchar(128) null,
    message longtext null,
    suggestion longtext null,
    source varchar(128) null,
    timestamp_utc datetime not null,
    updated_utc datetime not null,
    key ix_gateway_runtime_errors_project_time(project_id, timestamp_utc)
) character set utf8mb4 collate utf8mb4_unicode_ci;";

    private const string SqlServerRuntimeStateSchema = @"
if object_id(N'gateway_runtime_device_statuses', N'U') is null
begin
    create table gateway_runtime_device_statuses (
        id nvarchar(64) not null primary key,
        project_id nvarchar(64) not null,
        device_id nvarchar(64) not null,
        device_name nvarchar(128) not null,
        protocol nvarchar(64) not null,
        enabled bit not null,
        is_connected bit not null,
        status nvarchar(32) not null,
        consecutive_failures int not null,
        total_reads bigint not null,
        successful_reads bigint not null,
        failed_reads bigint not null,
        success_rate float not null,
        last_poll_utc datetime2 not null,
        last_success_utc datetime2 not null,
        last_failure_utc datetime2 not null,
        last_error nvarchar(max) null,
        updated_utc datetime2 not null
    );
end;

if not exists (select 1 from sys.indexes where name = N'ix_gateway_runtime_device_statuses_project' and object_id = object_id(N'gateway_runtime_device_statuses'))
    create index ix_gateway_runtime_device_statuses_project on gateway_runtime_device_statuses(project_id);

if object_id(N'gateway_runtime_tag_snapshots', N'U') is null
begin
    create table gateway_runtime_tag_snapshots (
        id nvarchar(64) not null primary key,
        project_id nvarchar(64) not null,
        device_id nvarchar(64) not null,
        device_name nvarchar(128) not null,
        group_id nvarchar(64) null,
        group_name nvarchar(128) null,
        tag_id nvarchar(64) not null,
        tag_name nvarchar(128) not null,
        data_type nvarchar(64) not null,
        raw_value_text nvarchar(max) null,
        value_text nvarchar(max) null,
        unit nvarchar(64) null,
        point_code nvarchar(256) null,
        source nvarchar(128) null,
        quality nvarchar(32) not null,
        cleaning_applied bit not null default(0),
        cleaning_action nvarchar(64) null,
        cleaning_message nvarchar(max) null,
        timestamp_utc datetime2 not null,
        error_message nvarchar(max) null,
        updated_utc datetime2 not null
    );
end;

if not exists (select 1 from sys.indexes where name = N'ix_gateway_runtime_tag_snapshots_project' and object_id = object_id(N'gateway_runtime_tag_snapshots'))
    create index ix_gateway_runtime_tag_snapshots_project on gateway_runtime_tag_snapshots(project_id);

if not exists (select 1 from sys.indexes where name = N'ix_gateway_runtime_tag_snapshots_tag' and object_id = object_id(N'gateway_runtime_tag_snapshots'))
    create index ix_gateway_runtime_tag_snapshots_tag on gateway_runtime_tag_snapshots(project_id, tag_id);

if object_id(N'gateway_runtime_errors', N'U') is null
begin
    create table gateway_runtime_errors (
        id nvarchar(64) not null primary key,
        project_id nvarchar(64) not null,
        category nvarchar(64) not null,
        device_name nvarchar(128) null,
        group_name nvarchar(128) null,
        tag_name nvarchar(128) null,
        message nvarchar(max) null,
        suggestion nvarchar(max) null,
        source nvarchar(128) null,
        timestamp_utc datetime2 not null,
        updated_utc datetime2 not null
    );
end;

if not exists (select 1 from sys.indexes where name = N'ix_gateway_runtime_errors_project_time' and object_id = object_id(N'gateway_runtime_errors'))
    create index ix_gateway_runtime_errors_project_time on gateway_runtime_errors(project_id, timestamp_utc desc);";

    private const string SqliteRuntimeStateSchema = @"
create table if not exists gateway_runtime_device_statuses (
    id text not null primary key,
    project_id text not null,
    device_id text not null,
    device_name text not null,
    protocol text not null,
    enabled integer not null,
    is_connected integer not null,
    status text not null,
    consecutive_failures integer not null,
    total_reads integer not null,
    successful_reads integer not null,
    failed_reads integer not null,
    success_rate real not null,
    last_poll_utc text not null,
    last_success_utc text not null,
    last_failure_utc text not null,
    last_error text null,
    updated_utc text not null
);

create index if not exists ix_gateway_runtime_device_statuses_project
    on gateway_runtime_device_statuses(project_id);

create table if not exists gateway_runtime_tag_snapshots (
    id text not null primary key,
    project_id text not null,
    device_id text not null,
    device_name text not null,
    group_id text null,
    group_name text null,
    tag_id text not null,
    tag_name text not null,
    data_type text not null,
    raw_value_text text null,
    value_text text null,
    unit text null,
    point_code text null,
    source text null,
    quality text not null,
    cleaning_applied integer not null default 0,
    cleaning_action text null,
    cleaning_message text null,
    timestamp_utc text not null,
    error_message text null,
    updated_utc text not null
);

create index if not exists ix_gateway_runtime_tag_snapshots_project
    on gateway_runtime_tag_snapshots(project_id);

create index if not exists ix_gateway_runtime_tag_snapshots_tag
    on gateway_runtime_tag_snapshots(project_id, tag_id);

create table if not exists gateway_runtime_errors (
    id text not null primary key,
    project_id text not null,
    category text not null,
    device_name text null,
    group_name text null,
    tag_name text null,
    message text null,
    suggestion text null,
    source text null,
    timestamp_utc text not null,
    updated_utc text not null
);

create index if not exists ix_gateway_runtime_errors_project_time
    on gateway_runtime_errors(project_id, timestamp_utc desc);";

    private const string PostgreSqlReconnectDelaySchema = @"
alter table gateway_runtime_device_statuses
    add column if not exists last_reconnect_delay_ms integer not null default 0;";

    private const string MySqlReconnectDelaySchema = @"
set @gateway_reconnect_delay_sql := (
    select if(count(*) = 0,
        'alter table gateway_runtime_device_statuses add column last_reconnect_delay_ms int not null default 0',
        'select 1')
    from information_schema.columns
    where table_schema = database()
      and table_name = 'gateway_runtime_device_statuses'
      and column_name = 'last_reconnect_delay_ms'
);
prepare gateway_reconnect_delay_stmt from @gateway_reconnect_delay_sql;
execute gateway_reconnect_delay_stmt;
deallocate prepare gateway_reconnect_delay_stmt;";

    private const string SqlServerReconnectDelaySchema = @"
if col_length(N'gateway_runtime_device_statuses', N'last_reconnect_delay_ms') is null
begin
    alter table gateway_runtime_device_statuses
        add last_reconnect_delay_ms int not null
            constraint df_gateway_runtime_device_statuses_last_reconnect_delay_ms default(0);
end;";

    private const string SqliteReconnectDelaySchema = @"
alter table gateway_runtime_device_statuses
    add column last_reconnect_delay_ms integer not null default 0;";

    private const string PostgreSqlRuntimeTagCleaningStateSchema = @"
alter table gateway_runtime_tag_snapshots
    add column if not exists cleaning_applied boolean not null default false;
alter table gateway_runtime_tag_snapshots
    add column if not exists cleaning_action text null;
alter table gateway_runtime_tag_snapshots
    add column if not exists cleaning_message text null;";

    private const string MySqlRuntimeTagCleaningStateSchema = @"
set @gateway_cleaning_applied_sql := (
    select if(count(*) = 0,
        'alter table gateway_runtime_tag_snapshots add column cleaning_applied bit not null default 0',
        'select 1')
    from information_schema.columns
    where table_schema = database()
      and table_name = 'gateway_runtime_tag_snapshots'
      and column_name = 'cleaning_applied'
);
prepare stmt from @gateway_cleaning_applied_sql;
execute stmt;
deallocate prepare stmt;
set @gateway_cleaning_action_sql := (
    select if(count(*) = 0,
        'alter table gateway_runtime_tag_snapshots add column cleaning_action varchar(64) null',
        'select 1')
    from information_schema.columns
    where table_schema = database()
      and table_name = 'gateway_runtime_tag_snapshots'
      and column_name = 'cleaning_action'
);
prepare stmt from @gateway_cleaning_action_sql;
execute stmt;
deallocate prepare stmt;
set @gateway_cleaning_message_sql := (
    select if(count(*) = 0,
        'alter table gateway_runtime_tag_snapshots add column cleaning_message longtext null',
        'select 1')
    from information_schema.columns
    where table_schema = database()
      and table_name = 'gateway_runtime_tag_snapshots'
      and column_name = 'cleaning_message'
);
prepare stmt from @gateway_cleaning_message_sql;
execute stmt;
deallocate prepare stmt;";

    private const string SqlServerRuntimeTagCleaningStateSchema = @"
if col_length(N'gateway_runtime_tag_snapshots', N'cleaning_applied') is null
begin
    alter table gateway_runtime_tag_snapshots
        add cleaning_applied bit not null
            constraint df_gateway_runtime_tag_snapshots_cleaning_applied default(0);
end;
if col_length(N'gateway_runtime_tag_snapshots', N'cleaning_action') is null
begin
    alter table gateway_runtime_tag_snapshots
        add cleaning_action nvarchar(64) null;
end;
if col_length(N'gateway_runtime_tag_snapshots', N'cleaning_message') is null
begin
    alter table gateway_runtime_tag_snapshots
        add cleaning_message nvarchar(max) null;
end;";

    private const string SqliteRuntimeTagCleaningStateSchema = @"
alter table gateway_runtime_tag_snapshots
    add column cleaning_applied integer not null default 0;
alter table gateway_runtime_tag_snapshots
    add column cleaning_action text null;
alter table gateway_runtime_tag_snapshots
    add column cleaning_message text null;";

    private const string PostgreSqlRoleManagementSchema = @"
create table if not exists gateway_roles (
    id text primary key,
    name text not null unique,
    display_name text null,
    description text null,
    enabled boolean not null,
    is_system boolean not null,
    permissions_json text not null,
    created_utc timestamp without time zone not null,
    updated_utc timestamp without time zone not null
);

create index if not exists ix_gateway_roles_name
    on gateway_roles(lower(name));

create index if not exists ix_gateway_roles_enabled
    on gateway_roles(enabled);";

    private const string MySqlRoleManagementSchema = @"
create table if not exists gateway_roles (
    id varchar(64) not null primary key,
    name varchar(64) not null unique,
    display_name varchar(128) null,
    description varchar(512) null,
    enabled bit not null,
    is_system bit not null,
    permissions_json longtext not null,
    created_utc datetime not null,
    updated_utc datetime not null,
    key ix_gateway_roles_name(name),
    key ix_gateway_roles_enabled(enabled)
) character set utf8mb4 collate utf8mb4_unicode_ci;";

    private const string SqlServerRoleManagementSchema = @"
if object_id(N'gateway_roles', N'U') is null
begin
    create table gateway_roles (
        id nvarchar(64) not null primary key,
        name nvarchar(64) not null unique,
        display_name nvarchar(128) null,
        description nvarchar(512) null,
        enabled bit not null,
        is_system bit not null,
        permissions_json nvarchar(max) not null,
        created_utc datetime2 not null,
        updated_utc datetime2 not null
    );
end;

if not exists (select 1 from sys.indexes where name = N'ix_gateway_roles_name' and object_id = object_id(N'gateway_roles'))
    create index ix_gateway_roles_name on gateway_roles(name);

if not exists (select 1 from sys.indexes where name = N'ix_gateway_roles_enabled' and object_id = object_id(N'gateway_roles'))
    create index ix_gateway_roles_enabled on gateway_roles(enabled);";

    private const string SqliteRoleManagementSchema = @"
create table if not exists gateway_roles (
    id text not null primary key,
    name text not null unique,
    display_name text null,
    description text null,
    enabled integer not null,
    is_system integer not null,
    permissions_json text not null,
    created_utc text not null,
    updated_utc text not null
);

create index if not exists ix_gateway_roles_name
    on gateway_roles(name);

create index if not exists ix_gateway_roles_enabled
    on gateway_roles(enabled);";

    private const string PostgreSqlAccountSecurityStateSchema = @"
alter table gateway_users
    add column if not exists password_changed_utc timestamp without time zone not null default timestamp '1970-01-01 00:00:00';
alter table gateway_users
    add column if not exists last_login_utc timestamp without time zone null;
alter table gateway_users
    add column if not exists last_failed_login_utc timestamp without time zone null;
alter table gateway_users
    add column if not exists failed_login_count integer not null default 0;
alter table gateway_users
    add column if not exists lockout_end_utc timestamp without time zone null;";

    private const string MySqlAccountSecurityStateSchema = @"
set @gateway_user_password_changed_sql := (
    select if(count(*) = 0,
        'alter table gateway_users add column password_changed_utc datetime not null default ''1970-01-01 00:00:00''',
        'select 1')
    from information_schema.columns
    where table_schema = database()
      and table_name = 'gateway_users'
      and column_name = 'password_changed_utc'
);
prepare stmt from @gateway_user_password_changed_sql;
execute stmt;
deallocate prepare stmt;
set @gateway_user_last_login_sql := (
    select if(count(*) = 0,
        'alter table gateway_users add column last_login_utc datetime null',
        'select 1')
    from information_schema.columns
    where table_schema = database()
      and table_name = 'gateway_users'
      and column_name = 'last_login_utc'
);
prepare stmt from @gateway_user_last_login_sql;
execute stmt;
deallocate prepare stmt;
set @gateway_user_last_failed_sql := (
    select if(count(*) = 0,
        'alter table gateway_users add column last_failed_login_utc datetime null',
        'select 1')
    from information_schema.columns
    where table_schema = database()
      and table_name = 'gateway_users'
      and column_name = 'last_failed_login_utc'
);
prepare stmt from @gateway_user_last_failed_sql;
execute stmt;
deallocate prepare stmt;
set @gateway_user_failed_count_sql := (
    select if(count(*) = 0,
        'alter table gateway_users add column failed_login_count int not null default 0',
        'select 1')
    from information_schema.columns
    where table_schema = database()
      and table_name = 'gateway_users'
      and column_name = 'failed_login_count'
);
prepare stmt from @gateway_user_failed_count_sql;
execute stmt;
deallocate prepare stmt;
set @gateway_user_lockout_end_sql := (
    select if(count(*) = 0,
        'alter table gateway_users add column lockout_end_utc datetime null',
        'select 1')
    from information_schema.columns
    where table_schema = database()
      and table_name = 'gateway_users'
      and column_name = 'lockout_end_utc'
);
prepare stmt from @gateway_user_lockout_end_sql;
execute stmt;
deallocate prepare stmt;";

    private const string SqlServerAccountSecurityStateSchema = @"
if col_length(N'gateway_users', N'password_changed_utc') is null
begin
    alter table gateway_users
        add password_changed_utc datetime2 not null
            constraint df_gateway_users_password_changed_utc default('1970-01-01T00:00:00');
end;
if col_length(N'gateway_users', N'last_login_utc') is null
begin
    alter table gateway_users add last_login_utc datetime2 null;
end;
if col_length(N'gateway_users', N'last_failed_login_utc') is null
begin
    alter table gateway_users add last_failed_login_utc datetime2 null;
end;
if col_length(N'gateway_users', N'failed_login_count') is null
begin
    alter table gateway_users
        add failed_login_count int not null
            constraint df_gateway_users_failed_login_count default(0);
end;
if col_length(N'gateway_users', N'lockout_end_utc') is null
begin
    alter table gateway_users add lockout_end_utc datetime2 null;
end;";

    private const string SqliteAccountSecurityStateSchema = @"
alter table gateway_users add column password_changed_utc text not null default '1970-01-01 00:00:00';
alter table gateway_users add column last_login_utc text null;
alter table gateway_users add column last_failed_login_utc text null;
alter table gateway_users add column failed_login_count integer not null default 0;
alter table gateway_users add column lockout_end_utc text null;";

    private const string PostgreSqlAuditLogSchema = @"
create table if not exists gateway_audit_logs (
    id text primary key,
    timestamp_utc timestamp without time zone not null,
    level text not null,
    action text not null,
    target text null,
    outcome text null,
    username text null,
    role text null,
    remote_ip_address text null,
    method text null,
    path text null,
    trace_id text null,
    error_message text null,
    raw_detail text null
);

create index if not exists ix_gateway_audit_logs_time
    on gateway_audit_logs(timestamp_utc desc);

create index if not exists ix_gateway_audit_logs_target_time
    on gateway_audit_logs(target, timestamp_utc desc);

create index if not exists ix_gateway_audit_logs_outcome_time
    on gateway_audit_logs(outcome, timestamp_utc desc);

create index if not exists ix_gateway_audit_logs_user_time
    on gateway_audit_logs(username, timestamp_utc desc);";

    private const string MySqlAuditLogSchema = @"
create table if not exists gateway_audit_logs (
    id varchar(64) not null primary key,
    timestamp_utc datetime not null,
    level varchar(32) not null,
    action varchar(128) not null,
    target varchar(256) null,
    outcome varchar(64) null,
    username varchar(128) null,
    role varchar(64) null,
    remote_ip_address varchar(128) null,
    method varchar(16) null,
    path varchar(512) null,
    trace_id varchar(128) null,
    error_message longtext null,
    raw_detail longtext null,
    key ix_gateway_audit_logs_time(timestamp_utc),
    key ix_gateway_audit_logs_target_time(target, timestamp_utc),
    key ix_gateway_audit_logs_outcome_time(outcome, timestamp_utc),
    key ix_gateway_audit_logs_user_time(username, timestamp_utc)
) character set utf8mb4 collate utf8mb4_unicode_ci;";

    private const string SqlServerAuditLogSchema = @"
if object_id(N'gateway_audit_logs', N'U') is null
begin
    create table gateway_audit_logs (
        id nvarchar(64) not null primary key,
        timestamp_utc datetime2 not null,
        level nvarchar(32) not null,
        action nvarchar(128) not null,
        target nvarchar(256) null,
        outcome nvarchar(64) null,
        username nvarchar(128) null,
        role nvarchar(64) null,
        remote_ip_address nvarchar(128) null,
        method nvarchar(16) null,
        path nvarchar(512) null,
        trace_id nvarchar(128) null,
        error_message nvarchar(max) null,
        raw_detail nvarchar(max) null
    );
end;

if not exists (select 1 from sys.indexes where name = N'ix_gateway_audit_logs_time' and object_id = object_id(N'gateway_audit_logs'))
    create index ix_gateway_audit_logs_time on gateway_audit_logs(timestamp_utc desc);

if not exists (select 1 from sys.indexes where name = N'ix_gateway_audit_logs_target_time' and object_id = object_id(N'gateway_audit_logs'))
    create index ix_gateway_audit_logs_target_time on gateway_audit_logs(target, timestamp_utc desc);

if not exists (select 1 from sys.indexes where name = N'ix_gateway_audit_logs_outcome_time' and object_id = object_id(N'gateway_audit_logs'))
    create index ix_gateway_audit_logs_outcome_time on gateway_audit_logs(outcome, timestamp_utc desc);

if not exists (select 1 from sys.indexes where name = N'ix_gateway_audit_logs_user_time' and object_id = object_id(N'gateway_audit_logs'))
    create index ix_gateway_audit_logs_user_time on gateway_audit_logs(username, timestamp_utc desc);";

    private const string SqliteAuditLogSchema = @"
create table if not exists gateway_audit_logs (
    id text not null primary key,
    timestamp_utc text not null,
    level text not null,
    action text not null,
    target text null,
    outcome text null,
    username text null,
    role text null,
    remote_ip_address text null,
    method text null,
    path text null,
    trace_id text null,
    error_message text null,
    raw_detail text null
);

create index if not exists ix_gateway_audit_logs_time
    on gateway_audit_logs(timestamp_utc desc);

create index if not exists ix_gateway_audit_logs_target_time
    on gateway_audit_logs(target, timestamp_utc desc);

create index if not exists ix_gateway_audit_logs_outcome_time
    on gateway_audit_logs(outcome, timestamp_utc desc);

create index if not exists ix_gateway_audit_logs_user_time
    on gateway_audit_logs(username, timestamp_utc desc);";

    private const string PostgreSqlRuntimeChannelIdentitySchema = @"
alter table gateway_runtime_device_statuses add column if not exists channel_id text null;
alter table gateway_runtime_device_statuses add column if not exists channel_name text null;
alter table gateway_runtime_tag_snapshots add column if not exists channel_id text null;
alter table gateway_runtime_tag_snapshots add column if not exists channel_name text null;
alter table gateway_runtime_errors add column if not exists channel_id text null;
alter table gateway_runtime_errors add column if not exists channel_name text null;
alter table gateway_runtime_errors add column if not exists device_id text null;
alter table gateway_runtime_errors add column if not exists group_id text null;
alter table gateway_runtime_errors add column if not exists tag_id text null;";

    private const string MySqlRuntimeChannelIdentitySchema = @"
alter table gateway_runtime_device_statuses
    add column channel_id varchar(64) null,
    add column channel_name varchar(128) null;
alter table gateway_runtime_tag_snapshots
    add column channel_id varchar(64) null,
    add column channel_name varchar(128) null;
alter table gateway_runtime_errors
    add column channel_id varchar(64) null,
    add column channel_name varchar(128) null,
    add column device_id varchar(64) null,
    add column group_id varchar(64) null,
    add column tag_id varchar(64) null;";

    private const string SqlServerRuntimeChannelIdentitySchema = @"
if col_length(N'gateway_runtime_device_statuses', N'channel_id') is null
    alter table gateway_runtime_device_statuses add channel_id nvarchar(64) null;
if col_length(N'gateway_runtime_device_statuses', N'channel_name') is null
    alter table gateway_runtime_device_statuses add channel_name nvarchar(128) null;
if col_length(N'gateway_runtime_tag_snapshots', N'channel_id') is null
    alter table gateway_runtime_tag_snapshots add channel_id nvarchar(64) null;
if col_length(N'gateway_runtime_tag_snapshots', N'channel_name') is null
    alter table gateway_runtime_tag_snapshots add channel_name nvarchar(128) null;
if col_length(N'gateway_runtime_errors', N'channel_id') is null
    alter table gateway_runtime_errors add channel_id nvarchar(64) null;
if col_length(N'gateway_runtime_errors', N'channel_name') is null
    alter table gateway_runtime_errors add channel_name nvarchar(128) null;
if col_length(N'gateway_runtime_errors', N'device_id') is null
    alter table gateway_runtime_errors add device_id nvarchar(64) null;
if col_length(N'gateway_runtime_errors', N'group_id') is null
    alter table gateway_runtime_errors add group_id nvarchar(64) null;
if col_length(N'gateway_runtime_errors', N'tag_id') is null
    alter table gateway_runtime_errors add tag_id nvarchar(64) null;";

    private const string SqliteRuntimeChannelIdentitySchema = @"
alter table gateway_runtime_device_statuses add column channel_id text null;
alter table gateway_runtime_device_statuses add column channel_name text null;
alter table gateway_runtime_tag_snapshots add column channel_id text null;
alter table gateway_runtime_tag_snapshots add column channel_name text null;
alter table gateway_runtime_errors add column channel_id text null;
alter table gateway_runtime_errors add column channel_name text null;
alter table gateway_runtime_errors add column device_id text null;
alter table gateway_runtime_errors add column group_id text null;
alter table gateway_runtime_errors add column tag_id text null;";
}
