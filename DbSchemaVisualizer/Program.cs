using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;

namespace DbSchemaVisualizer
{
    class Program
    {
        static void Main()
        {
            string connectionString = "Data Source=localhost\\SQLEXPRESS;Initial Catalog=AdventureWorks;Integrated Security=True;";
            string dotFile = "graph_output.dot";
            string svgFile = "graph_output.svg";
            string htmlFile = "index.html";

            var tablePKs = new Dictionary<string, List<string>>();
            var edges = new List<Tuple<string, string, string, string>>();

            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();

                string pkQuery = @"
                    SELECT t.name AS TableName, c.name AS ColumnName
                    FROM sys.tables t
                    INNER JOIN sys.columns c ON t.object_id = c.object_id
                    INNER JOIN sys.indexes i ON t.object_id = i.object_id
                    INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND c.column_id = ic.column_id AND i.index_id = ic.index_id
                    WHERE i.is_primary_key = 1
                    ORDER BY t.name, ic.key_ordinal;";

                using (var cmd = new SqlCommand(pkQuery, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string table = reader.GetString(0);
                        string column = reader.GetString(1);
                        if (!tablePKs.ContainsKey(table))
                            tablePKs[table] = new List<string>();
                        tablePKs[table].Add(column);
                    }
                }

                string fkQuery = @"
                    SELECT
                        tp.name AS ParentTable,
                        cp.name AS ParentColumn,
                        tr.name AS ReferencedTable,
                        cr.name AS ReferencedColumn
                    FROM sys.foreign_keys fk
                    INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
                    INNER JOIN sys.tables tp ON fkc.parent_object_id = tp.object_id
                    INNER JOIN sys.columns cp ON fkc.parent_object_id = cp.object_id AND fkc.parent_column_id = cp.column_id
                    INNER JOIN sys.tables tr ON fkc.referenced_object_id = tr.object_id
                    INNER JOIN sys.columns cr ON fkc.referenced_object_id = cr.object_id AND fkc.referenced_column_id = cr.column_id;";

                using (var cmd = new SqlCommand(fkQuery, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string parentTable = reader.GetString(0);
                        string parentColumn = reader.GetString(1);
                        string referencedTable = reader.GetString(2);
                        string referencedColumn = reader.GetString(3);
                        edges.Add(Tuple.Create(parentTable, referencedTable, parentColumn, referencedColumn));
                    }
                }
            }
////////////////// this block defines and styles the nodes 
            using (StreamWriter writer = new StreamWriter(dotFile))
            {
                writer.WriteLine("digraph schema {");
                // Corrected line: 'node' attributes should be defined within a 'node [...]' block.
                // We keep 'shape=plaintext' as a base, but the HTML label dictates the actual shape.
                writer.WriteLine("  node [shape=plaintext fontname=\"Segoe UI\"];");

                foreach (var kvp in tablePKs)
                {
                    string table = kvp.Key;
                    List<string> pkColumns = kvp.Value;

                    writer.WriteLine($"  \"{table}\" [label=<");

                    writer.WriteLine("  <table border=\"0\" cellborder=\"1\" cellspacing=\"0\" cellpadding=\"10\" bgcolor=\"#ffffff\">");

                    // Header row
                    writer.WriteLine("    <tr><td bgcolor=\"#1f497d\" sides=\"tb\" align=\"center\" port=\"header\"><font color=\"white\"><b>" + table + "</b></font></td></tr>");

                    // PK rows
                    foreach (var column in pkColumns)
                    {
                        writer.WriteLine("    <tr><td bgcolor=\"#ffffef\" align=\"center\" port=\"" + column + "\"><font color=\"#1f497d\"><b>" + column + "</b></font></td></tr>");
                    }

                    writer.WriteLine("  </table>");
                    writer.WriteLine(">];");
                }






                //////////////// this block fully customize the edges/////////////
                foreach (var edge in edges)
                {
                    string tooltipMessage = "<b>each <b>" + edge.Item1 + "</b> may have multiple <b>" + edge.Item2 + "</b></b>";
                    string tooltipUri = "data:text/html," + Uri.EscapeDataString(tooltipMessage);

                    writer.WriteLine("  " + edge.Item1 + " -> " + edge.Item2 + " [");
                    writer.WriteLine("    color=\"#b9cde5\",");  //the color of the edge
                    writer.WriteLine("    penwidth=2.5,");
                    writer.WriteLine("    arrowhead=dot,"); //sets the style of the arrowhead at the end of the edge in Graphviz.
                    writer.WriteLine("    arrowsize=1.5,");
                    writer.WriteLine("    style=solid,");
                    writer.WriteLine("    URL=\"" + tooltipUri + "\"");
                    writer.WriteLine("  ];");
                }

                writer.WriteLine("}");
            }

            Console.WriteLine("✅ DOT file created.");
            RunGraphviz("-Tsvg " + dotFile + " -o " + svgFile);
            InjectHtmlWithTooltip(svgFile, htmlFile);
            Console.WriteLine("✅ HTML with custom tooltips generated.");
        }
/////////
        
        static void RunGraphviz(string args)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = "dot";
            startInfo.Arguments = args;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;

            using (Process process = Process.Start(startInfo))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    Console.WriteLine("❌ Graphviz error:");
                    Console.WriteLine(error);
                }
            }
        }

        static void InjectHtmlWithTooltip(string svgPath, string htmlPath)
        {
            string svg = File.ReadAllText(svgPath);

            string html = @"<!DOCTYPE html>
<html>
<head>
  <meta charset='UTF-8'>
  <title>Schema</title>
  <style>
    html, body { margin: 0; padding: 0; width: 100%; height: 100%; overflow: hidden; }
    #container {
      width: 100vw;
      height: 100vh;
      overflow: auto;
    }
    svg {
      width: 2000px;
      height: 2000px;
    }
    #custom-tooltip {
      position: fixed;
      display: none;
      padding: 6px 12px;
      background-color: #fce4cf;
      color: #003366;
      border-radius: 10px;
      box-shadow: 0 2px 4px rgba(0,0,0,0.15);
      font-family: 'Segoe UI', sans-serif;
      font-size: 14px;
      z-index: 1000;
      white-space: nowrap;
      pointer-events: none;
    }
  </style>
</head>
<body>
<div id='container'>
" + svg + @"
</div>
<div id='custom-tooltip'></div>
<script>
  document.querySelectorAll('g.edge title').forEach(el => el.remove());
  document.querySelectorAll('g.edge a').forEach(a => {
    a.removeAttribute('xlink:title');
    a.removeAttribute('title');
  });
  const tooltip = document.getElementById('custom-tooltip');
  const edges = document.querySelectorAll('g.edge');

  edges.forEach(edge => {
    const a = edge.querySelector('a');
    if (!a) return;
    const href = a.getAttribute('xlink:href');
    if (!href || !href.startsWith('data:text/html,')) return;

    const message = decodeURIComponent(href.slice('data:text/html,'.length));

    edge.addEventListener('mouseenter', e => {
      tooltip.innerHTML = message;
      tooltip.style.display = 'block';
      tooltip.style.left = e.pageX + 8 + 'px';
      tooltip.style.top = e.pageY + 8 + 'px';
    });

    edge.addEventListener('mousemove', e => {
      tooltip.style.left = e.pageX + 8 + 'px';
      tooltip.style.top = e.pageY + 8 + 'px';
    });

    edge.addEventListener('mouseleave', () => {
      tooltip.style.display = 'none';
    });
  });
</script>
</body>
</html>";

            File.WriteAllText(htmlPath, html);
        }
    }
}
