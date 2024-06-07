using FishNet.Transporting.KCP.Edgegap;
using FishNet.Transporting.KCP.Editor;
using UnityEditor;
using UnityEngine;

namespace FishNet.Transporting.Edgegap.KCP.Editor
{
    [CustomEditor(typeof(EdgegapKcpTransport), true)]
    public class EdgegapKcpTransportEditor : KcpTransportEditor
    {
        private SerializedProperty _protocolType;

        protected override void OnEnable()
        {
            _protocolType = serializedObject.FindProperty(nameof(_protocolType));
            base.OnEnable();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var transport = (EdgegapKcpTransport)target;

            GUI.enabled = false;
            EditorGUILayout.ObjectField("Script:", MonoScript.FromMonoBehaviour(transport), typeof(EdgegapKcpTransport), false);
            GUI.enabled = true;

            ProtocolType protocol = transport.Protocol;
   
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_protocolType);
            EditorGUILayout.PropertyField(_noDelay);
            if (protocol != ProtocolType.EdgegapRelay)
            {
                EditorGUILayout.PropertyField(_mtu, new GUIContent("MTU"));
            }
            EditorGUILayout.PropertyField(_receiveBufferSize);
            EditorGUILayout.PropertyField(_sendBufferSize);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space();

            if (protocol != ProtocolType.EdgegapRelay)
            {
                EditorGUILayout.LabelField("Server", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_bindAddressIPv4, new GUIContent("IPv4 Bind Address"));
                EditorGUILayout.PropertyField(_enableIPv6, new GUIContent("Enable IPv6"));
                if (_enableIPv6.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_bindAddressIPv6, new GUIContent("IPv6 Bind Address"));
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.PropertyField(_port);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();

                EditorGUILayout.LabelField("Client", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_clientAddress);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            EditorGUILayout.LabelField("Advanced", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_fastResend);
            if (protocol != ProtocolType.EdgegapRelay)
            {
                EditorGUILayout.PropertyField(_congestionWindow);
            }
            EditorGUILayout.PropertyField(_receiveWindowSize);
            EditorGUILayout.PropertyField(_sendWindowSize);
            EditorGUILayout.PropertyField(_maxRetransmits);
            EditorGUI.indentLevel--;

            serializedObject.ApplyModifiedProperties();
        }
    }
}