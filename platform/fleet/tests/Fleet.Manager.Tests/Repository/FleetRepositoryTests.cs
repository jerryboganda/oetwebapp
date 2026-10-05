using System.Text.RegularExpressions;
using Fleet.Manager.Tests.Infrastructure;

namespace Fleet.Manager.Tests.Repository;

/// <summary>
/// Static guarantees about everything that ships from platform/fleet (OET-RWP/1 sections 7.4, 8.8, 8.9, 10): the compose project
/// cannot touch production, no host-key trust-on-first-use anywhere, only ansible.builtin, no secret in the tree, no shell in
/// the manager. These read files only; nothing is executed.
/// </summary>
public sealed class FleetRepositoryTests
{
    private static string Root => RepoPaths.FleetRoot();

    /// <summary>File text with line endings normalised, so the checks mean the same on a Windows checkout and on the Linux runner.</summary>
    private static string Text(string path) => File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Read(params string[] relative) => Text(Path.Combine(new[] { Root }.Concat(relative).ToArray()));

    /// <summary>Whole-line comments removed, so documentation that NAMES a forbidden thing ("never oetwebsite_*") is not mistaken for using it.</summary>
    private static string WithoutComments(string text) =>
        string.Join('\n', text.Split('\n').Where(line => !line.TrimStart().StartsWith('#')));

    private static string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    private static IEnumerable<string> Playbooks() =>
        Directory.EnumerateFiles(Path.Combine(Root, "ansible", "playbooks"), "*.yml", SearchOption.AllDirectories);

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !Relative(path).Contains("/obj/", StringComparison.Ordinal) && !Relative(path).Contains("/bin/", StringComparison.Ordinal));

    // ---- solution --------------------------------------------------------------------------

    [Fact]
    public void The_solution_registers_the_manager_the_core_the_agent_and_their_test_projects()
    {
        var solution = Read("Fleet.sln");

        foreach (var project in new[]
                 {
                     "src\\Fleet.Core\\Fleet.Core.csproj",
                     "src\\Fleet.Manager\\Fleet.Manager.csproj",
                     "src\\Fleet.Agent\\Fleet.Agent.csproj",
                     "tests\\Fleet.Manager.Tests\\Fleet.Manager.Tests.csproj",
                     "tests\\Fleet.Agent.Tests\\Fleet.Agent.Tests.csproj",
                 })
        {
            Assert.Contains("\"" + project + "\"", solution);
        }

        // The projects this track owns exist; the agent projects belong to the agent track and are only registered here.
        foreach (var project in new[] { "src/Fleet.Core/Fleet.Core.csproj", "src/Fleet.Manager/Fleet.Manager.csproj", "tests/Fleet.Manager.Tests/Fleet.Manager.Tests.csproj" })
        {
            Assert.True(File.Exists(Path.Combine(Root, project)), project + " is missing");
        }

        var ids = Regex.Matches(solution, "\\{6D3F1B52-[0-9A-F-]+\\}").Select(m => m.Value).Distinct().ToList();
        Assert.Equal(5, ids.Count);
    }

    [Fact]
    public void The_manager_targets_dotnet_10_and_uses_the_web_sdk_with_sqlite_only()
    {
        var manager = Read("src", "Fleet.Manager", "Fleet.Manager.csproj");

        Assert.Contains("Microsoft.NET.Sdk.Web", manager);
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", manager);
        Assert.Contains("Microsoft.EntityFrameworkCore.Sqlite", manager);
        Assert.DoesNotContain("Npgsql", manager);
        Assert.DoesNotContain("SqlServer", manager);
        Assert.Equal(1, Regex.Matches(manager, "<PackageReference ").Count);
        Assert.Contains("<Nullable>enable</Nullable>", manager);
    }

    // ---- compose project -------------------------------------------------------------------

    [Fact]
    public void The_compose_project_is_its_own_with_one_service_no_build_and_no_production_reference()
    {
        var compose = WithoutComments(Read("docker-compose.fleet.yml"));

        Assert.Matches("(?m)^name: oet-fleet$", compose);
        Assert.Single(Regex.Matches(compose, "(?m)^  [a-z][a-z-]*:\\s*$").Where(m => m.Value.Trim() == "fleet-manager:"));
        Assert.Matches("image: \\$\\{FLEET_MANAGER_IMAGE:\\?", compose);
        foreach (var forbidden in new[] { "build:", "docker.sock", "oetwebsite", "npm_proxy", "oet_agent_", "network_mode", "privileged", "cap_add", "pid: host", "ipc: host", "devices:", "extra_hosts" })
        {
            Assert.DoesNotContain(forbidden, compose);
        }
    }

    [Fact]
    public void The_only_published_port_is_on_loopback_and_the_only_volume_is_the_external_fleet_volume()
    {
        var compose = WithoutComments(Read("docker-compose.fleet.yml"));

        var ports = Regex.Match(compose, "(?m)^    ports:\\s*\\n(?<items>(?:      - .+\\n)+)");
        Assert.True(ports.Success, "no ports block");
        var entries = ports.Groups["items"].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()).ToArray();
        var entry = Assert.Single(entries);
        Assert.StartsWith("- \"127.0.0.1:", entry);
        Assert.DoesNotContain("0.0.0.0", compose.Replace("http://0.0.0.0:8080", string.Empty, StringComparison.Ordinal));

        var volumes = Regex.Match(compose, "(?m)^    volumes:\\s*\\n(?<items>(?:      - .+\\n)+)");
        Assert.True(volumes.Success, "no volumes block");
        Assert.Equal("- fleet_data:/data", Assert.Single(volumes.Groups["items"].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim())));
        Assert.Matches("(?s)volumes:\\s+fleet_data:\\s+external: true\\s+name: oet-fleet_fleet_data", compose);
    }

    [Fact]
    public void The_container_runs_hardened_unprivileged_limited_and_is_the_first_thing_the_kernel_kills_under_memory_pressure()
    {
        var compose = WithoutComments(Read("docker-compose.fleet.yml"));

        Assert.Contains("user: \"10020:10020\"", compose);
        Assert.Contains("read_only: true", compose);
        Assert.Contains("cap_drop: [ALL]", compose);
        Assert.Contains("no-new-privileges:true", compose);
        Assert.Contains("init: true", compose);
        Assert.Contains("restart: unless-stopped", compose);
        Assert.Matches("mem_limit: \\d+[mg]", compose);
        Assert.Matches("memswap_limit: \\d+[mg]", compose);
        Assert.Matches("cpus: [0-9.]+", compose);
        Assert.Matches("pids_limit: \\d+", compose);

        // Postgres and the API slots keep oom_score_adj 0 (or negative); this container must be a clearly higher number.
        var score = int.Parse(Regex.Match(compose, "oom_score_adj: (?<n>\\d+)").Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(score, 500, 1000);

        // Writable space is exactly /data (the volume) and tmpfs.
        Assert.Contains("/tmp:rw,noexec,nosuid,nodev", compose);
        Assert.DoesNotContain("exec,", compose.Replace("noexec,", string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void The_manager_listens_on_all_interfaces_only_in_container_mode_where_compose_publishes_it_on_loopback()
    {
        var compose = WithoutComments(Read("docker-compose.fleet.yml"));

        Assert.Contains("ASPNETCORE_URLS: http://0.0.0.0:8080", compose);
        Assert.Contains("Fleet__Binding__Mode: container", compose);
        Assert.Contains("Fleet__Data__Directory: /data", compose);
        Assert.Contains("Fleet__Secrets__Directory: /run/secrets", compose);
        Assert.Contains("healthcheck:", compose);
        Assert.Contains("\"dotnet\", \"/app/Fleet.Manager.dll\", \"healthcheck\"", compose);
    }

    [Fact]
    public void Secrets_are_compose_secret_files_never_environment_values_and_no_value_in_the_file_looks_like_a_credential()
    {
        var compose = WithoutComments(Read("docker-compose.fleet.yml"));

        foreach (var secret in new[] { "fleet_master_key", "fleet_master_key_prev", "fleet_api_credential", "fleet_sync_token", "fleet_metrics_token" })
        {
            Assert.Matches("(?m)^      - " + secret + "$", compose);
            Assert.Matches("(?m)^  " + secret + ":\\s*\\n    file: ", compose);
        }

        Assert.DoesNotMatch("(ghp_|ghs_|github_pat_|orw1_|ofs1_|BEGIN [A-Z ]*PRIVATE KEY)", compose);

        // The environment block names no secret variable (the *_FILE secrets are mounted, not passed).
        var environment = Regex.Match(compose, "(?ms)^    environment:\\s*\\n(?<items>.*?)^    secrets:").Groups["items"].Value;
        Assert.False(string.IsNullOrWhiteSpace(environment));
        Assert.DoesNotMatch("(?im)^\\s+[A-Za-z_]*(PASSWORD|TOKEN|CREDENTIAL|MASTER_KEY|PRIVATE)[A-Za-z_]*:", environment);
    }

    [Fact]
    public void The_service_is_attached_to_its_own_bridge_only_for_outbound_traffic()
    {
        var compose = WithoutComments(Read("docker-compose.fleet.yml"));

        Assert.Matches("(?m)^    networks:\\s*\\n      - oet_fleet_net$", compose);
        Assert.Matches("(?ms)^networks:\\s*\\n  oet_fleet_net:\\s*\\n    name: oet_fleet_net\\s*\\n    driver: bridge", compose);
        Assert.DoesNotContain("external: true\n    name: oet_fleet_net", compose);
        Assert.Equal(1, Regex.Matches(compose, "(?m)^      - oet_fleet_net$").Count);
    }

    // ---- image -----------------------------------------------------------------------------

    [Fact]
    public void The_image_is_built_by_the_pipeline_from_the_dotnet_10_images_runs_as_uid_10020_and_adds_four_packages()
    {
        var dockerfile = WithoutComments(Read("Dockerfile"));

        Assert.Matches("(?m)^FROM mcr\\.microsoft\\.com/dotnet/sdk:10\\.0 AS build$", dockerfile);
        Assert.Matches("(?m)^FROM mcr\\.microsoft\\.com/dotnet/aspnet:10\\.0 AS runtime$", dockerfile);
        Assert.Contains("apt-get install -y --no-install-recommends ansible-core openssh-client python3 ca-certificates", dockerfile);
        Assert.Contains("rm -rf /var/lib/apt/lists/*", dockerfile);
        Assert.Contains("USER 10020:10020", dockerfile);
        Assert.Contains("ENTRYPOINT [\"dotnet\", \"/app/Fleet.Manager.dll\"]", dockerfile);
        Assert.Contains("HEALTHCHECK", dockerfile);
        Assert.Contains("COPY ansible /opt/fleet/ansible", dockerfile);
        Assert.Contains("dotnet publish", dockerfile);
        Assert.Contains("--no-restore", dockerfile);
        Assert.Equal(2, Regex.Matches(dockerfile, "(?m)^FROM ").Count);
        foreach (var forbidden in new[] { "curl ", "wget ", "sudo", "docker.io", "docker-ce", "pip install", "ADD http", "--privileged", "USER root", "apt-get upgrade" })
        {
            Assert.DoesNotContain(forbidden, dockerfile);
        }
    }

    [Fact]
    public void The_build_context_leaves_tests_docs_state_and_secrets_out_of_the_image()
    {
        var ignore = Read(".dockerignore").Split('\n').Select(line => line.Trim()).ToHashSet(StringComparer.Ordinal);

        foreach (var entry in new[] { "**/bin/", "**/obj/", "tests/", "*.md", "**/*.db", "**/id_*", "**/known_hosts*", "**/*.pem", "**/*.key" })
        {
            Assert.Contains(entry, ignore);
        }

        Assert.DoesNotContain("ansible/", ignore);
        Assert.DoesNotContain("src/", ignore);
    }

    [Fact]
    public void Git_never_sees_state_keys_trust_files_secrets_or_real_inventories()
    {
        var ignore = Read(".gitignore").Split('\n').Select(line => line.Trim()).ToHashSet(StringComparer.Ordinal);

        foreach (var entry in new[] { "*.db", "*.sqlite", "id_*", "*.pem", "*.key", "known_hosts", "fleet_master_key*", "fleet_api_credential*", "fleet_sync_token*", "fleet_metrics_token*", "secrets/", ".env", "ansible/inventory/*", "!ansible/inventory/target.yml", "bin/", "obj/" })
        {
            Assert.Contains(entry, ignore);
        }

        var gitattributes = Read(".gitattributes");
        Assert.Contains("ansible/files/* text eol=lf", gitattributes);
        Assert.Contains("Dockerfile text eol=lf", gitattributes);
    }

    [Fact]
    public void The_tree_holds_no_state_key_or_secret_file()
    {
        var offenders = Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Select(Relative)
            .Where(path => !path.StartsWith("src/Fleet.Manager/bin/", StringComparison.Ordinal)
                           && !path.Contains("/obj/", StringComparison.Ordinal)
                           && !path.Contains("/bin/", StringComparison.Ordinal))
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return name.EndsWith(".db", StringComparison.Ordinal)
                       || name.EndsWith(".sqlite", StringComparison.Ordinal)
                       || name.EndsWith(".pem", StringComparison.Ordinal)
                       || name.EndsWith(".key", StringComparison.Ordinal)
                       || name.StartsWith("id_", StringComparison.Ordinal)
                       || name.StartsWith("known_hosts", StringComparison.Ordinal)
                       || name.StartsWith("fleet_master_key", StringComparison.Ordinal)
                       || name.StartsWith("fleet_api_credential", StringComparison.Ordinal)
                       || name.StartsWith(".env", StringComparison.Ordinal);
            })
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_readme_documents_what_the_code_and_the_compose_file_point_to()
    {
        var readme = Read("README.md");

        foreach (var heading in new[] { "## First deployment", "## Container hardening", "## Database policy", "## Security model", "## Helper side", "## Deviations from the spec" })
        {
            Assert.Contains(heading, readme);
        }
    }

    // ---- trust and supply chain ------------------------------------------------------------

    [Fact]
    public void No_shipped_file_ever_trusts_a_host_key_on_first_use_or_turns_host_key_checking_off()
    {
        var forbidden = new[]
        {
            "accept" + "-new",
            "StrictHostKeyChecking=no",
            "StrictHostKeyChecking no",
            "StrictHostKeyChecking=ask",
            "UserKnownHostsFile=/dev/null",
            "host_key_checking = False",
            "host_key_checking=False",
            "ANSIBLE_HOST_KEY_CHECKING=False",
            "\"ANSIBLE_HOST_KEY_CHECKING\"] = \"False\"",
            "VerifyHostKeyDNS=yes",
        };

        var scanned = 0;
        foreach (var path in RepoPaths.ShippedFiles())
        {
            scanned++;
            var text = Text(path);
            foreach (var pattern in forbidden)
            {
                Assert.False(text.Contains(pattern, StringComparison.OrdinalIgnoreCase), Relative(path) + " contains '" + pattern + "'");
            }
        }

        Assert.True(scanned > 40, "the scan found suspiciously few files: " + scanned);
        Assert.Contains("host_key_checking = True", Read("ansible", "ansible.cfg"));
        Assert.Contains("StrictHostKeyChecking=yes", Read("src", "Fleet.Core", "Ssh", "SshOptions.cs"));
    }

    [Fact]
    public void No_shipped_file_contains_a_credential_shaped_string()
    {
        var shapes = new[]
        {
            new Regex("-----BEGIN [A-Z ]*PRIVATE KEY-----\\s*[A-Za-z0-9+/=\\s]{40,}", RegexOptions.CultureInvariant),
            new Regex("\\bgh[pousr]_[A-Za-z0-9]{16,}", RegexOptions.CultureInvariant),
            new Regex("\\bgithub_pat_[A-Za-z0-9_]{20,}", RegexOptions.CultureInvariant),
            new Regex("\\bo(?:rw|fs)1_[0-9a-f]{16}_[A-Za-z0-9_-]{43}", RegexOptions.CultureInvariant),
            new Regex("\\bAKIA[0-9A-Z]{16}\\b", RegexOptions.CultureInvariant),
            new Regex("\\bsk-[A-Za-z0-9_-]{20,}", RegexOptions.CultureInvariant),
            new Regex("eyJ[A-Za-z0-9_-]{10,}\\.[A-Za-z0-9_-]{10,}\\.[A-Za-z0-9_-]{10,}", RegexOptions.CultureInvariant),
        };

        foreach (var path in RepoPaths.ShippedFiles())
        {
            var text = Text(path);
            foreach (var shape in shapes)
            {
                Assert.False(shape.IsMatch(text), Relative(path) + " matches a credential shape: " + shape);
            }
        }
    }

    // ---- ansible ---------------------------------------------------------------------------

    [Fact]
    public void Every_playbook_uses_only_ansible_builtin_modules_so_nothing_is_downloaded_from_galaxy()
    {
        var module = new Regex("(?m)^\\s*(?:-\\s+)?(?<name>[a-z][a-z0-9_]*\\.[a-z][a-z0-9_]*\\.[a-z][a-z0-9_]*):", RegexOptions.CultureInvariant);
        var playbooks = Playbooks().ToList();
        Assert.True(playbooks.Count >= 8, "expected the seven step playbooks and the shared task file");

        foreach (var path in playbooks)
        {
            var names = module.Matches(Text(path)).Select(m => m.Groups["name"].Value).Distinct().ToList();
            Assert.NotEmpty(names);
            Assert.All(names, name => Assert.True(name.StartsWith("ansible.builtin.", StringComparison.Ordinal), Relative(path) + " uses " + name));
        }

        Assert.DoesNotContain("collections_path", Read("ansible", "ansible.cfg"));
        Assert.False(Directory.Exists(Path.Combine(Root, "ansible", "collections")));
        Assert.False(File.Exists(Path.Combine(Root, "ansible", "requirements.yml")));
    }

    [Fact]
    public void The_playbooks_never_download_or_pipe_code_and_never_touch_other_containers_images_volumes_or_networks()
    {
        foreach (var path in Playbooks())
        {
            var text = WithoutComments(Text(path));
            var name = Relative(path);

            Assert.False(Regex.IsMatch(text, "\\b(curl|wget)\\b[^\\n]*\\|\\s*(ba)?sh"), name + " pipes a download into a shell");
            Assert.False(Regex.IsMatch(text, "\\|\\s*(ba)?sh\\b"), name + " pipes into a shell");
            Assert.False(Regex.IsMatch(text, "(?m)^\\s*(?:-\\s+)?ansible\\.builtin\\.(raw|script|pip|git|unarchive|expect|uri):"), name + " uses a module that runs or fetches arbitrary code");
            Assert.False(Regex.IsMatch(text, "\\bdocker\\s+(rm|rmi|stop|kill|restart|volume|system|network|compose|run|exec|build|pull|image|container|prune|cp|commit|save|load)\\b"), name + " touches Docker objects");
            Assert.DoesNotContain("/var/lib/docker", text);
            Assert.DoesNotContain("/opt/oetwebapp", text);
            Assert.False(Regex.IsMatch(text, "\\b(iptables|ufw|firewall-cmd)\\b"), name + " uses another firewall than the fleet nftables table");
            Assert.False(Regex.IsMatch(text, "\\bnft\\s+flush\\b"), name + " flushes the whole ruleset (Docker owns tables here)");
        }

        // Production is named in exactly one place: the read-only preflight probe that REFUSES a host running it.
        var naming = Playbooks().Where(path => Text(path).Contains("oetwebsite", StringComparison.Ordinal)).Select(Relative).ToList();
        Assert.Equal(new[] { "ansible/playbooks/preflight.yml" }, naming.ToArray());

        // Downloads exist in one place only: the optional vendor Docker repository key, pinned by fingerprint.
        var downloads = Playbooks().Where(path => Text(path).Contains("get_url", StringComparison.Ordinal)).Select(Relative).ToList();
        Assert.Equal(new[] { "ansible/playbooks/docker.yml" }, downloads.ToArray());
        var docker = Read("ansible", "playbooks", "docker.yml");
        Assert.Contains("9DC858229FC7DD38854AE2D88D81803C0EBFCD88", docker);
        Assert.All(
            Regex.Matches(docker, "https://[^\\s\"']+").Select(m => m.Value),
            url => Assert.StartsWith("https://download.docker.com/", url));
    }

    [Fact]
    public void Every_step_playbook_targets_one_host_gathers_nothing_and_records_a_result_on_success_and_failure()
    {
        var steps = new[] { "preflight", "fleet-user", "install-key", "docker", "firewall", "host-baseline", "harden-ssh" };

        foreach (var step in steps)
        {
            var text = Read("ansible", "playbooks", step + ".yml");

            Assert.Matches("(?m)^  hosts: target$", text);
            Assert.Matches("(?m)^  gather_facts: false$", text);
            Assert.DoesNotContain("hosts: all", text);
            Assert.DoesNotContain("hosts: localhost", text);
            Assert.True(Regex.Matches(text, "include_tasks: tasks/record-result\\.yml").Count >= 2, step + " must record both outcomes");
            Assert.Contains("rescue:", text);
            Assert.Contains("ansible.builtin.fail:", text);
        }

        var record = Read("ansible", "playbooks", "tasks", "record-result.yml");
        Assert.Contains("fleet_result_file", record);
        Assert.Contains("mode: \"0600\"", record);
        Assert.Contains("changed_when: false", record);
    }

    [Fact]
    public void Tasks_that_run_on_the_manager_never_become_root_and_the_only_ones_are_the_connection_probes_and_the_result_file()
    {
        var permitted = new[] { "wait_for", "command", "copy" };

        foreach (var path in Playbooks())
        {
            var lines = Text(path).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Trim().Equals("delegate_to: localhost", StringComparison.Ordinal))
                {
                    continue;
                }

                var window = string.Join('\n', lines.Skip(Math.Max(0, i - 3)).Take(12));
                Assert.Contains("become: false", window);
                Assert.True(
                    permitted.Any(module => window.Contains("ansible.builtin." + module + ":", StringComparison.Ordinal)),
                    Relative(path) + " line " + (i + 1) + ": a controller-side task uses an unexpected module");
            }
        }
    }

    [Fact]
    public void Destructive_state_in_the_playbooks_is_limited_to_the_sshd_rollback_and_the_firewall_table_rollback()
    {
        foreach (var path in Playbooks())
        {
            var text = Text(path);
            var name = Relative(path);
            var absent = Regex.Matches(text, "state: absent").Count;

            if (name.EndsWith("harden-ssh.yml", StringComparison.Ordinal))
            {
                Assert.Equal(1, absent);
                Assert.Contains("path: \"{{ fleet_dropin }}\"", text);
            }
            else
            {
                Assert.Equal(0, absent);
            }

            Assert.DoesNotContain("state: latest", text);
            Assert.DoesNotContain("autoremove", text);
            Assert.False(Regex.IsMatch(text, "\\brm\\s+-"), name + " removes files with rm");
        }

        Assert.Contains("nft delete table inet oet_fleet", Read("ansible", "playbooks", "firewall.yml"));
    }

    [Fact]
    public void The_inventory_is_a_template_with_no_host_data_and_the_config_keeps_host_key_checking_on()
    {
        var inventory = Read("ansible", "inventory", "target.yml");

        Assert.DoesNotMatch("\\b\\d{1,3}\\.\\d{1,3}\\.\\d{1,3}\\.\\d{1,3}\\b", inventory);
        Assert.Contains("{{ fleet_target_address }}", inventory);
        Assert.Contains("{{ fleet_ssh_common_args }}", inventory);
        Assert.Single(Regex.Matches(inventory, "(?m)^    target:$"));
        Assert.DoesNotContain("password", inventory, StringComparison.OrdinalIgnoreCase);

        var config = Read("ansible", "ansible.cfg");
        Assert.Contains("host_key_checking = True", config);
        Assert.Contains("gathering = explicit", config);
        Assert.DoesNotContain("become_ask_pass", config);
        Assert.DoesNotContain("vault_password_file", config);
        Assert.Equal(new[] { "inventory/target.yml" }, Directory.EnumerateFiles(Path.Combine(Root, "ansible", "inventory")).Select(p => Relative(p).Replace("ansible/", string.Empty, StringComparison.Ordinal)).ToArray());
    }

    // ---- the manager's own code ------------------------------------------------------------

    [Fact]
    public void The_manager_starts_child_processes_in_exactly_one_place_and_never_through_a_shell()
    {
        var starters = SourceFiles()
            .Where(path => Text(path).Contains("ProcessStartInfo", StringComparison.Ordinal) || Text(path).Contains("Process.Start(", StringComparison.Ordinal))
            .Select(Relative)
            .ToList();
        Assert.Equal(new[] { "src/Fleet.Manager/Provisioning/ProcessRunner.cs" }, starters.ToArray());

        foreach (var path in SourceFiles())
        {
            var text = Text(path);
            var name = Relative(path);
            Assert.DoesNotContain("UseShellExecute = true", text);
            Assert.False(Regex.IsMatch(text, "\"(/bin/)?(ba|z|da)?sh\"|\"cmd(\\.exe)?\"|\"powershell(\\.exe)?\"|\"/bin/sh\""), name + " names a shell");
        }
    }

    [Fact]
    public void The_manager_reads_no_environment_variable_except_path_and_the_listen_url_and_opens_http_connections_in_one_place()
    {
        foreach (var path in SourceFiles())
        {
            var text = Text(path);
            foreach (Match match in Regex.Matches(text, "GetEnvironmentVariable\\(\"(?<name>[^\"]+)\"\\)"))
            {
                Assert.Contains(match.Groups["name"].Value, new[] { "PATH", "ASPNETCORE_URLS" });
            }
        }

        var clients = SourceFiles().Where(path => Regex.IsMatch(Text(path), "new HttpClient\\b")).Select(Relative).ToList();
        Assert.Equal(new[] { "src/Fleet.Manager/Cli/FleetCli.cs" }, clients.ToArray());

        var registration = Read("src", "Fleet.Manager", "Hosting", "ServiceRegistration.cs");
        Assert.Contains("AllowAutoRedirect = false", registration);
    }

    [Fact]
    public void Sql_and_log_statements_are_never_built_by_string_interpolation()
    {
        foreach (var path in SourceFiles())
        {
            var text = Text(path);
            var name = Relative(path);

            Assert.False(Regex.IsMatch(text, "(ExecuteSql|FromSql)\\w*\\(\\s*\\$"), name + " interpolates SQL");
            Assert.False(Regex.IsMatch(text, "CommandText\\s*=\\s*\\$\""), name + " interpolates SQL");
            Assert.False(Regex.IsMatch(text, "\\.Log(Trace|Debug|Information|Warning|Error|Critical)\\(\\s*\\$\""), name + " logs an interpolated string");
        }
    }

    [Fact]
    public void The_manager_never_writes_a_secret_to_the_console_except_the_one_time_totp_secret_of_owner_init()
    {
        foreach (var path in SourceFiles())
        {
            var text = Text(path);
            var name = Relative(path);
            var lines = text.Split('\n').Where(line => Regex.IsMatch(line, "Console\\.(Error\\.)?Write")).ToList();

            if (!name.EndsWith("Cli/FleetCli.cs", StringComparison.Ordinal))
            {
                Assert.Empty(lines);
                continue;
            }

            Assert.DoesNotContain(lines, line => line.Contains("Token", StringComparison.Ordinal) && !line.Contains("TOTP secret", StringComparison.Ordinal) && !line.Contains("sync token file", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, line => line.Contains("password", StringComparison.OrdinalIgnoreCase) && !line.Contains("owner", StringComparison.OrdinalIgnoreCase));
        }
    }
}
