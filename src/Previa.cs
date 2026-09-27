// Rubinite: vista previa del daño de la Estocada Crítica en la barra de vida del jefe.
//
// BossUI.Update() llama a Previa.Actualizar(this) en cada fotograma (el instalador inserta esa llamada).
// Sobre el segmento visible de la barra se dibujan dos copias de la imagen HpBar:
//   - una amarilla, rellena hasta la vida actual
//   - una roja (color original) rellena hasta la vida que quedaría tras la Estocada
// de modo que el tramo amarillo es exactamente el daño que harías con las marcas acumuladas.
// El daño se obtiene con la propia fórmula del juego (PlayerCore.CalculateATKOfStrike).

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace RubinitePreviaDanio
{
    public static class Previa
    {
        static readonly Color ColorPrevia = new Color(1f, 0.82f, 0.25f, 1f);
        static readonly FieldInfo CampoIndice = typeof(BossUI).GetField("index", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo CampoOculta = typeof(BossUI).GetField("alwaysHide", BindingFlags.Instance | BindingFlags.NonPublic);

        class Capas { public Image Previa; public Image Encima; }

        static readonly Dictionary<Image, Capas> capas = new Dictionary<Image, Capas>();
        static readonly Dictionary<BossUI, BasicEnemyCore> dueñoBarraExtra = new Dictionary<BossUI, BasicEnemyCore>();
        static float siguienteBusqueda;
        static bool errorRegistrado;

        public static void Actualizar(BossUI ui)
        {
            try { ActualizarInterno(ui); }
            catch (Exception e)
            {
                if (!errorRegistrado) { errorRegistrado = true; Debug.LogWarning("[RubinitePreviaDanio] " + e); }
            }
        }

        static void ActualizarInterno(BossUI ui)
        {
            if (ui == null || ui.hPDetails == null || ui.hPDetails.Count == 0) return;

            int total = Mathf.Clamp(ui.HPCount, 1, ui.hPDetails.Count);
            int indice = CampoIndice != null ? (int)CampoIndice.GetValue(ui) : 0;
            indice = Mathf.Clamp(indice, 0, total - 1);

            float danio = 0f;
            bool oculta = CampoOculta != null && (bool)CampoOculta.GetValue(ui);
            if (!oculta && !ui.isFilling)
                danio = DanioEstocada(ui);

            for (int i = 0; i < ui.hPDetails.Count; i++)
            {
                BossHPUIDetail d = ui.hPDetails[i];
                if (d == null || d.HpBar == null) continue;
                if (i != indice || danio <= 0f || !d.HpBar.gameObject.activeInHierarchy)
                {
                    Ocultar(d.HpBar);
                    continue;
                }
                float min = i + 1 < total && ui.hPDetails[i + 1] != null ? ui.hPDetails[i + 1].maxHP : 0f;
                float max = d.maxHP;
                if (max - min <= 0.0001f) { Ocultar(d.HpBar); continue; }
                float actual = d.HpBar.fillAmount;
                float despues = Mathf.Clamp01((ui.currentHP - danio - min) / (max - min));
                if (despues >= actual) { Ocultar(d.HpBar); continue; }
                Mostrar(d.HpBar, actual, despues);
            }
        }

        static float DanioEstocada(BossUI ui)
        {
            GamePlayCore gp = GamePlayCore.Instance;
            if (gp == null || gp.player == null || gp.levelManager == null) return 0f;
            PlayerCore jugador = gp.player;

            EnemyCoreBase enemigo = null;
            FocusItem objetivo = null;
            if (gp.levelManager.bossUI == ui)
            {
                enemigo = gp.levelManager.boss;
                if (enemigo != null) objetivo = enemigo.focusItem;
                if (objetivo == null || objetivo.currentFocusCount <= 0)
                    objetivo = jugador.currentFocusTarget;          // jefes con varias partes (manos, núcleos...)
            }
            else
            {
                BasicEnemyCore dueño = DueñoBarraExtra(ui);         // p. ej. los Cultistas Gemelos
                enemigo = dueño;
                if (dueño != null) objetivo = dueño.focusItem;
            }
            if (objetivo == null || enemigo != null && enemigo.isDead) return 0f;

            int marcas = objetivo.currentFocusCount;
            if (marcas <= 0) return 0f;
            if (jugador.Attr != null && jugador.Attr.strikeConsumeSingleMark) marcas = 1;   // talismán Maestría de Marcas

            float danio = jugador.CalculateATKOfStrike(marcas, objetivo);
            BasicEnemyCore basico = enemigo as BasicEnemyCore;
            if (basico != null && basico.isSecondRun && basico.enemyAttr != null)
                danio *= basico.enemyAttr.secondRunHurtScale;
            return danio;
        }

        static BasicEnemyCore DueñoBarraExtra(BossUI ui)
        {
            BasicEnemyCore dueño;
            if (dueñoBarraExtra.TryGetValue(ui, out dueño) && dueño != null) return dueño;
            if (Time.unscaledTime < siguienteBusqueda) return null;
            siguienteBusqueda = Time.unscaledTime + 1f;
            foreach (BasicEnemyCore e in UnityEngine.Object.FindObjectsByType<BasicEnemyCore>(FindObjectsSortMode.None))
                if (e.extraBossHPBar == ui) { dueñoBarraExtra[ui] = e; return e; }
            return null;
        }

        static void Mostrar(Image barra, float actual, float despues)
        {
            Capas c = ObtenerCapas(barra);
            float alfaBase = barra.color.a * barra.canvasRenderer.GetAlpha();
            float pulso = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 8f);

            Color cp = ColorPrevia; cp.a = alfaBase * pulso;
            c.Previa.color = cp;
            c.Previa.fillAmount = actual;

            c.Encima.color = barra.color;
            c.Encima.fillAmount = despues;

            if (!c.Previa.gameObject.activeSelf) c.Previa.gameObject.SetActive(true);
            if (!c.Encima.gameObject.activeSelf) c.Encima.gameObject.SetActive(true);
        }

        static void Ocultar(Image barra)
        {
            Capas c;
            if (!capas.TryGetValue(barra, out c)) return;
            if (c.Previa != null && c.Previa.gameObject.activeSelf) c.Previa.gameObject.SetActive(false);
            if (c.Encima != null && c.Encima.gameObject.activeSelf) c.Encima.gameObject.SetActive(false);
        }

        static Capas ObtenerCapas(Image barra)
        {
            Capas c;
            if (capas.TryGetValue(barra, out c) && c.Previa != null && c.Encima != null) return c;
            c = new Capas();
            c.Previa = Copiar(barra, "PreviaDanio");
            c.Encima = Copiar(barra, "PreviaDanio_Restante");
            int pos = barra.transform.GetSiblingIndex();
            c.Previa.transform.SetSiblingIndex(pos + 1);
            c.Encima.transform.SetSiblingIndex(pos + 2);
            capas[barra] = c;
            return c;
        }

        static Image Copiar(Image barra, string nombre)
        {
            GameObject go = new GameObject(nombre, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform origen = (RectTransform)barra.transform;
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(origen.parent, false);
            rt.anchorMin = origen.anchorMin;
            rt.anchorMax = origen.anchorMax;
            rt.pivot = origen.pivot;
            rt.sizeDelta = origen.sizeDelta;
            rt.anchoredPosition3D = origen.anchoredPosition3D;
            rt.localRotation = origen.localRotation;
            rt.localScale = origen.localScale;
            go.layer = barra.gameObject.layer;

            Image img = go.GetComponent<Image>();
            img.sprite = barra.sprite;
            img.material = barra.material;
            img.type = Image.Type.Filled;
            img.fillMethod = barra.fillMethod;
            img.fillOrigin = barra.fillOrigin;
            img.preserveAspect = barra.preserveAspect;
            img.raycastTarget = false;
            go.SetActive(false);
            return img;
        }
    }
}
