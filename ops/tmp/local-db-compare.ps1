$env:PGPASSWORD = 'postgres'
$psql = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
$dbs = @('oet_with_dr_hesham', 'oet_with_dr_hesham_dev', 'oet_with_dr_hesham_proof')
foreach ($db in $dbs) {
    Write-Output "===== $db ====="
    $tbls = (& $psql -U postgres -h localhost -p 5432 -d $db -tAc "select count(*) from pg_stat_user_tables" 2>$null)
    Write-Output "TableCount: $tbls"
    $totalrows = (& $psql -U postgres -h localhost -p 5432 -d $db -tAc "select coalesce(sum(n_live_tup),0) from pg_stat_user_tables" 2>$null)
    Write-Output "TotalLiveRows: $totalrows"
    $migs = (& $psql -U postgres -h localhost -p 5432 -d $db -tAc 'select count(*) from "__EFMigrationsHistory"' 2>$null)
    Write-Output "Migrations: $migs"
    $lastmig = (& $psql -U postgres -h localhost -p 5432 -d $db -tAc 'select "MigrationId" from "__EFMigrationsHistory" order by "MigrationId" desc limit 1' 2>$null)
    Write-Output "LastMig: $lastmig"
    $users = (& $psql -U postgres -h localhost -p 5432 -d $db -tAc 'select count(*) from "Users"' 2>$null)
    Write-Output "Users: $users"
}
