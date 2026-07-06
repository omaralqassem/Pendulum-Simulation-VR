using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

public class RuntimeInspector : MonoBehaviour
{
    [Header("Targets")]
    [Tooltip("Specific components to expose. If empty and autoFindAllInScene is true, all MonoBehaviours in the scene are used.")]
    public List<MonoBehaviour> targets = new List<MonoBehaviour>();

    [Tooltip("If true and 'targets' is empty, automatically find every MonoBehaviour in the active scene.")]
    public bool autoFindAllInScene = true;

    [Header("Window")]
    public KeyCode toggleKey = KeyCode.F1;
    public bool startVisible = true;
    public Vector2 windowPosition = new Vector2(20, 20);
    public Vector2 windowSize = new Vector2(340, 500);

    private bool _visible;
    private Vector2 _scroll;
    private Rect _windowRect;
    private string _search = "";

    private readonly Dictionary<MonoBehaviour, List<MemberInfo>> _memberCache = new Dictionary<MonoBehaviour, List<MemberInfo>>();
    private readonly Dictionary<string, string> _textCache = new Dictionary<string, string>();

    private const int WINDOW_ID = 918273;

    private void Awake()
    {
        _visible = startVisible;
        _windowRect = new Rect(windowPosition.x, windowPosition.y, windowSize.x, windowSize.y);
        RefreshTargets();
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            _visible = !_visible;
    }

    public void RefreshTargets()
    {
        _memberCache.Clear();

        List<MonoBehaviour> resolvedTargets = targets != null && targets.Count > 0
            ? targets
            : (autoFindAllInScene ? FindAllMonoBehavioursInScene() : new List<MonoBehaviour>());

        foreach (var mb in resolvedTargets)
        {
            if (mb == null) continue;
            _memberCache[mb] = GetEditableMembers(mb.GetType());
        }
    }

    private List<MonoBehaviour> FindAllMonoBehavioursInScene()
    {
#if UNITY_2023_1_OR_NEWER
        return FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .Where(m => !(m is RuntimeInspector))
            .ToList();
#else
        return FindObjectsOfType<MonoBehaviour>()
            .Where(m => !(m is RuntimeInspector))
            .ToList();
#endif
    }

    private static List<MemberInfo> GetEditableMembers(Type type)
    {
        var result = new List<MemberInfo>();

        // Public instance fields
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => IsSupportedType(f.FieldType));
        result.AddRange(fields);

        // Public instance properties with both get and set
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0
                        && IsSupportedType(p.PropertyType));
        result.AddRange(props);

        return result;
    }

    private static bool IsSupportedType(Type t)
    {
        return t == typeof(int) || t == typeof(float) || t == typeof(bool) ||
               t == typeof(string) || t.IsEnum ||
               t == typeof(Vector2) || t == typeof(Vector3) || t == typeof(Vector4) ||
               t == typeof(Color);
    }

    private void OnGUI()
    {
        if (!_visible) return;
        _windowRect = GUI.Window(WINDOW_ID, _windowRect, DrawWindow, "Runtime Inspector");
    }

    private void DrawWindow(int id)
    {
        GUI.DragWindow(new Rect(0, 0, _windowRect.width, 20));

        GUILayout.BeginHorizontal();
        GUILayout.Label("Search:", GUILayout.Width(50));
        _search = GUILayout.TextField(_search);
        if (GUILayout.Button("Refresh", GUILayout.Width(65)))
            RefreshTargets();
        GUILayout.EndHorizontal();

        _scroll = GUILayout.BeginScrollView(_scroll);

        foreach (var kvp in _memberCache)
        {
            var component = kvp.Key;
            if (component == null) continue;
            var members = kvp.Value;
            if (members.Count == 0) continue;

            GUILayout.Space(6);
            GUILayout.Label($"{component.gameObject.name} / {component.GetType().Name}", GUI.skin.box);

            foreach (var member in members)
            {
                if (!string.IsNullOrEmpty(_search) &&
                    member.Name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                DrawMember(component, member);
            }
        }

        GUILayout.EndScrollView();

        if (GUILayout.Button($"Close ({toggleKey})"))
            _visible = false;
    }

    private void DrawMember(MonoBehaviour component, MemberInfo member)
    {
        Type valueType = GetMemberType(member);
        object currentValue = GetValue(component, member);
        string key = $"{component.GetInstanceID()}.{member.Name}";

        GUILayout.BeginHorizontal();
        GUILayout.Label(member.Name, GUILayout.Width(120));

        if (valueType == typeof(bool))
        {
            bool b = (bool)currentValue;
            bool newB = GUILayout.Toggle(b, "");
            if (newB != b) SetValue(component, member, newB);
        }
        else if (valueType == typeof(int))
        {
            int i = (int)currentValue;
            string text = GUILayout.TextField(i.ToString());
            if (int.TryParse(text, out int parsed) && parsed != i)
                SetValue(component, member, parsed);
        }
        else if (valueType == typeof(float))
        {
            float f = (float)currentValue;
            RangeAttribute range = GetRangeAttribute(member);
            if (range != null)
            {
                float newF = GUILayout.HorizontalSlider(f, range.min, range.max, GUILayout.Width(140));
                GUILayout.Label(newF.ToString("F2"), GUILayout.Width(45));
                if (!Mathf.Approximately(newF, f)) SetValue(component, member, newF);
            }
            else
            {
                string text = GUILayout.TextField(f.ToString("F3"));
                if (float.TryParse(text, out float parsed) && !Mathf.Approximately(parsed, f))
                    SetValue(component, member, parsed);
            }
        }
        else if (valueType == typeof(string))
        {
            string s = (string)currentValue ?? "";
            string newS = GUILayout.TextField(s);
            if (newS != s) SetValue(component, member, newS);
        }
        else if (valueType.IsEnum)
        {
            var names = Enum.GetNames(valueType);
            int currentIndex = Array.IndexOf(names, currentValue.ToString());
            int newIndex = GUILayoutSelector(currentIndex, names);
            if (newIndex != currentIndex)
                SetValue(component, member, Enum.Parse(valueType, names[newIndex]));
        }
        else if (valueType == typeof(Vector2))
        {
            Vector2 v = (Vector2)currentValue;
            v.x = ParseFloatField(v.x, key + ".x");
            v.y = ParseFloatField(v.y, key + ".y");
            SetValue(component, member, v);
        }
        else if (valueType == typeof(Vector3))
        {
            Vector3 v = (Vector3)currentValue;
            v.x = ParseFloatField(v.x, key + ".x");
            v.y = ParseFloatField(v.y, key + ".y");
            v.z = ParseFloatField(v.z, key + ".z");
            SetValue(component, member, v);
        }
        else if (valueType == typeof(Vector4))
        {
            Vector4 v = (Vector4)currentValue;
            v.x = ParseFloatField(v.x, key + ".x");
            v.y = ParseFloatField(v.y, key + ".y");
            v.z = ParseFloatField(v.z, key + ".z");
            v.w = ParseFloatField(v.w, key + ".w");
            SetValue(component, member, v);
        }
        else if (valueType == typeof(Color))
        {
            Color c = (Color)currentValue;
            c.r = ParseFloatField(c.r, key + ".r", 40);
            c.g = ParseFloatField(c.g, key + ".g", 40);
            c.b = ParseFloatField(c.b, key + ".b", 40);
            c.a = ParseFloatField(c.a, key + ".a", 40);
            SetValue(component, member, c);
        }

        GUILayout.EndHorizontal();
    }

    private int GUILayoutSelector(int currentIndex, string[] names)
    {
        if (currentIndex < 0) currentIndex = 0;
        if (GUILayout.Button(names[currentIndex], GUILayout.Width(120)))
            return (currentIndex + 1) % names.Length;
        return currentIndex;
    }

    private float ParseFloatField(float value, string key, int width = 55)
    {
        string text = GUILayout.TextField(value.ToString("F2"), GUILayout.Width(width));
        return float.TryParse(text, out float parsed) ? parsed : value;
    }

    private static Type GetMemberType(MemberInfo member)
    {
        return member is FieldInfo f ? f.FieldType : ((PropertyInfo)member).PropertyType;
    }

    private static object GetValue(object target, MemberInfo member)
    {
        return member is FieldInfo f ? f.GetValue(target) : ((PropertyInfo)member).GetValue(target);
    }

    private static void SetValue(object target, MemberInfo member, object value)
    {
        if (member is FieldInfo f) f.SetValue(target, value);
        else ((PropertyInfo)member).SetValue(target, value);
    }

    private static RangeAttribute GetRangeAttribute(MemberInfo member)
    {
        return member.GetCustomAttribute<RangeAttribute>();
    }
}
