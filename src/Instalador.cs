// DamagePreview: instalador de la vista previa del daño para Rubinite.
// Copia RubiniteDamagePreview.dll a Rubinite_Data\Managed e inserta al inicio de BossUI.Update():
//     RubiniteDamagePreview.Previa.Actualizar(this);
// Desinstalar quita esa llamada y el DLL. No toca nada más, así que convive con otros mods.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Win32;

static class Programa
{
    const string Ayudante = "RubiniteDamagePreview";
    static bool conMenu;

    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            new AssemblyName(e.Name).Name == "Mono.Cecil" ? Assembly.Load(Recurso("Mono.Cecil.dll")) : null;
        try { return Ejecutar(args); }
        catch (Exception e)
        {
            Console.WriteLine();
            Console.WriteLine("Ocurrió un error: " + e.Message);
            Pausa();
            return 1;
        }
    }

    static int Ejecutar(string[] args)
    {
        Console.WriteLine("=== Rubinite: vista previa del daño de la Estocada ===");
        Console.WriteLine();
        string ruta = args.FirstOrDefault(a => !a.StartsWith("--"));
        string juego = BuscarJuego(ruta);
        if (juego == null)
        {
            Console.WriteLine("No encontré el juego. Arrastra la carpeta de Rubinite sobre el programa.");
            Pausa(); return 1;
        }
        Console.WriteLine("Juego: " + juego);
        Console.WriteLine();

        string accion = args.Contains("--instalar") ? "1" : args.Contains("--desinstalar") ? "2" : null;
        if (accion == null)
        {
            conMenu = true;
            Console.WriteLine("  1) Instalar");
            Console.WriteLine("  2) Desinstalar");
            Console.WriteLine();
            Console.Write("Elige 1 o 2 y presiona Enter: ");
            accion = (Console.ReadLine() ?? "").Trim();
            Console.WriteLine();
        }
        if (accion != "1" && accion != "2") { Console.WriteLine("Opción no válida."); Pausa(); return 1; }
        if (JuegoAbierto(juego))
        {
            Console.WriteLine("El juego está abierto. Ciérralo y vuelve a intentarlo.");
            Pausa(); return 1;
        }

        string managed = Path.Combine(juego, @"Rubinite_Data\Managed");
        if (accion == "1") Parche.Instalar(managed, Recurso(Ayudante + ".dll"));
        else Parche.Desinstalar(managed);
        Pausa();
        return 0;
    }

    // Solo cuenta si el Rubinite.exe abierto es el de esta carpeta (se puede tener más de una copia).
    static bool JuegoAbierto(string juego)
    {
        string esperado = Path.GetFullPath(Path.Combine(juego, "Rubinite.exe"));
        foreach (Process p in Process.GetProcessesByName("Rubinite"))
        {
            try
            {
                if (string.Equals(Path.GetFullPath(p.MainModule.FileName), esperado, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { return true; }   // si no se puede saber, mejor no arriesgar
        }
        return false;
    }

    public static byte[] Recurso(string nombre)
    {
        using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(nombre))
        using (MemoryStream m = new MemoryStream()) { s.CopyTo(m); return m.ToArray(); }
    }

    static void Pausa()
    {
        if (!conMenu) return;
        Console.WriteLine();
        Console.Write("Presiona Enter para cerrar...");
        Console.ReadLine();
    }

    static string BuscarJuego(string arg)
    {
        List<string> candidatos = new List<string>();
        if (arg != null)
        {
            // Si se indica una ruta, solo se usa esa: nunca se cae a otra instalación.
            candidatos.Add(arg);
            candidatos.Add(Path.GetDirectoryName(arg) ?? "");
            return candidatos.FirstOrDefault(c => c != "" && File.Exists(Path.Combine(c, @"Rubinite_Data\Managed\Assembly-CSharp.dll")));
        }
        List<string> raices = new List<string>();
        foreach (string clave in new[] { @"HKEY_CURRENT_USER\Software\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam" })
            foreach (string valor in new[] { "SteamPath", "InstallPath" })
            {
                string v = Registry.GetValue(clave, valor, null) as string;
                if (!string.IsNullOrEmpty(v)) raices.Add(v.Replace('/', '\\'));
            }
        foreach (string raiz in raices.ToArray())
        {
            string vdf = Path.Combine(raiz, @"steamapps\libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    raices.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }
        foreach (char u in "CDEFGHIJ")
        {
            raices.Add(u + @":\Program Files (x86)\Steam");
            raices.Add(u + @":\SteamLibrary");
            raices.Add(u + @":\Steam");
        }
        candidatos.AddRange(raices.Select(r => Path.Combine(r, @"steamapps\common\Rubinite")));
        return candidatos.FirstOrDefault(c => c != "" && File.Exists(Path.Combine(c, @"Rubinite_Data\Managed\Assembly-CSharp.dll")));
    }
}

static class Parche
{
    const string Ayudante = "RubiniteDamagePreview";
    // Nombre que usaba la primera versión; se reconoce para actualizar o desinstalar sin dejar restos.
    static readonly string[] Nombres = { Ayudante, "RubinitePreviaDanio" };

    static Mono.Cecil.AssemblyDefinition Leer(string managed, string ruta)
    {
        var resolvedor = new Mono.Cecil.DefaultAssemblyResolver();
        resolvedor.AddSearchDirectory(managed);
        var p = new Mono.Cecil.ReaderParameters { AssemblyResolver = resolvedor, InMemory = true, ReadingMode = Mono.Cecil.ReadingMode.Immediate };
        return Mono.Cecil.AssemblyDefinition.ReadAssembly(ruta, p);
    }

    static Mono.Cecil.MethodDefinition UpdateDeBossUI(Mono.Cecil.AssemblyDefinition juego)
    {
        var tipo = juego.MainModule.GetType("BossUI");
        if (tipo == null) throw new Exception("No encontré BossUI (¿cambió el juego?).");
        var m = tipo.Methods.FirstOrDefault(x => x.Name == "Update" && !x.HasParameters);
        if (m == null || !m.HasBody) throw new Exception("No encontré BossUI.Update (¿cambió el juego?).");
        return m;
    }

    static bool EsNuestraLlamada(Mono.Cecil.Cil.Instruction i)
    {
        var mr = i.Operand as Mono.Cecil.MethodReference;
        return i.OpCode == Mono.Cecil.Cil.OpCodes.Call && mr != null && Nombres.Contains(mr.DeclaringType.Namespace);
    }

    static void Guardar(Mono.Cecil.AssemblyDefinition juego, string ruta)
    {
        string tmp = ruta + ".tmp_previa";
        juego.Write(tmp);
        File.Copy(tmp, ruta, true);
        File.Delete(tmp);
    }

    public static void Instalar(string managed, byte[] ayudante)
    {
        string rutaAyudante = Path.Combine(managed, Ayudante + ".dll");
        File.WriteAllBytes(rutaAyudante, ayudante);
        string anterior = Path.Combine(managed, "RubinitePreviaDanio.dll");
        if (File.Exists(anterior)) File.Delete(anterior);

        string ruta = Path.Combine(managed, "Assembly-CSharp.dll");
        using (var juego = Leer(managed, ruta))
        {
            var update = UpdateDeBossUI(juego);
            var existente = update.Body.Instructions.FirstOrDefault(EsNuestraLlamada);
            if (existente != null && ((Mono.Cecil.MethodReference)existente.Operand).DeclaringType.Namespace == Ayudante)
            {
                Console.WriteLine("Ya estaba instalado (se actualizó " + Ayudante + ".dll).");
                return;
            }
            QuitarLlamadas(juego, update);           // versión anterior con otro nombre
            using (var mod = Mono.Cecil.ModuleDefinition.ReadModule(new MemoryStream(ayudante)))
            {
                var metodo = mod.GetType(Ayudante + ".Previa").Methods.First(x => x.Name == "Actualizar");
                var importado = juego.MainModule.ImportReference(metodo);
                var il = update.Body.GetILProcessor();
                var primera = update.Body.Instructions[0];
                il.InsertBefore(primera, il.Create(Mono.Cecil.Cil.OpCodes.Ldarg_0));
                il.InsertBefore(primera, il.Create(Mono.Cecil.Cil.OpCodes.Call, importado));
            }
            Guardar(juego, ruta);
        }
        Console.WriteLine("Listo: la barra de los jefes mostrará en amarillo el daño de tu próxima Estocada.");
    }

    static bool QuitarLlamadas(Mono.Cecil.AssemblyDefinition juego, Mono.Cecil.MethodDefinition update)
    {
        bool habia = false;
        var cuerpo = update.Body.Instructions;
        for (int i = cuerpo.Count - 1; i >= 0; i--)
        {
            if (!EsNuestraLlamada(cuerpo[i])) continue;
            habia = true;
            cuerpo.RemoveAt(i);
            if (i > 0 && cuerpo[i - 1].OpCode == Mono.Cecil.Cil.OpCodes.Ldarg_0) cuerpo.RemoveAt(i - 1);
        }
        var refs = juego.MainModule.AssemblyReferences;
        foreach (var r in refs.Where(x => Nombres.Contains(x.Name)).ToList()) refs.Remove(r);
        return habia;
    }

    public static void Desinstalar(string managed)
    {
        string ruta = Path.Combine(managed, "Assembly-CSharp.dll");
        bool habia = false;
        using (var juego = Leer(managed, ruta))
        {
            habia = QuitarLlamadas(juego, UpdateDeBossUI(juego));
            if (habia) Guardar(juego, ruta);
        }
        foreach (string n in Nombres)
        {
            string rutaAyudante = Path.Combine(managed, n + ".dll");
            if (File.Exists(rutaAyudante)) { File.Delete(rutaAyudante); habia = true; }
        }
        Console.WriteLine(habia ? "Listo: se quitó la vista previa de daño." : "No estaba instalado; no hay nada que quitar.");
    }
}
