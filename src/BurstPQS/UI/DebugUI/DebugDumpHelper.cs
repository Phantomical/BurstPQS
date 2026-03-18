using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BurstPQS.UI.DebugUI;

internal static class DebugDumpHelper
{
    internal static string WriteDumpLog(string filename, StringBuilder sb)
    {
        var dir = Path.Combine(KSPUtil.ApplicationRootPath, "Logs", "BurstPQS");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, filename);
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    internal static void DumpGameObjectLayout(StringBuilder sb, GameObject go, int depth)
    {
        var indent = new string(' ', depth * 2);
        sb.Append($"{indent}\"{go.name}\" (active={go.activeSelf})");

        var rt = go.GetComponent<RectTransform>();
        if (rt != null)
            sb.Append(
                $" rect={rt.rect} anchorMin={rt.anchorMin} anchorMax={rt.anchorMax} offsetMin={rt.offsetMin} offsetMax={rt.offsetMax} pivot={rt.pivot}"
            );

        sb.AppendLine();

        foreach (var component in go.GetComponents<Component>())
        {
            if (component == null)
            {
                sb.AppendLine($"{indent}  [missing script]");
                continue;
            }

            if (component is RectTransform)
                continue;

            sb.Append($"{indent}  {component.GetType().Name}");

            switch (component)
            {
                case LayoutElement le:
                    sb.Append(
                        $" minW={le.minWidth} minH={le.minHeight} prefW={le.preferredWidth} prefH={le.preferredHeight} flexW={le.flexibleWidth} flexH={le.flexibleHeight} ignoreLayout={le.ignoreLayout}"
                    );
                    break;
                case VerticalLayoutGroup vlg:
                    sb.Append(
                        $" ctrlW={vlg.childControlWidth} ctrlH={vlg.childControlHeight} expandW={vlg.childForceExpandWidth} expandH={vlg.childForceExpandHeight} spacing={vlg.spacing} align={vlg.childAlignment} pad={vlg.padding}"
                    );
                    break;
                case HorizontalLayoutGroup hlg:
                    sb.Append(
                        $" ctrlW={hlg.childControlWidth} ctrlH={hlg.childControlHeight} expandW={hlg.childForceExpandWidth} expandH={hlg.childForceExpandHeight} spacing={hlg.spacing} align={hlg.childAlignment} pad={hlg.padding}"
                    );
                    break;
                case ContentSizeFitter csf:
                    sb.Append($" horiz={csf.horizontalFit} vert={csf.verticalFit}");
                    break;
                case TextMeshProUGUI tmp:
                    sb.Append(
                        $" text=\"{Truncate(tmp.text, 40)}\" fontSize={tmp.fontSize} overflow={tmp.overflowMode} wrapping={tmp.enableWordWrapping}"
                    );
                    break;
                case ScrollRect sr:
                    sb.Append($" horiz={sr.horizontal} vert={sr.vertical}");
                    break;
                case Image img:
                    sb.Append($" color={img.color} raycast={img.raycastTarget}");
                    break;
            }

            sb.AppendLine();
        }

        for (int i = 0; i < go.transform.childCount; i++)
            DumpGameObjectLayout(sb, go.transform.GetChild(i).gameObject, depth + 1);
    }

    private static string Truncate(string s, int max)
    {
        if (s == null)
            return "(null)";
        return s.Length <= max ? s : s.Substring(0, max) + "...";
    }
}
