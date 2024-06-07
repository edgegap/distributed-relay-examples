using UnityEditor;
using UnityEngine;

namespace FishNet.Transporting.KCP.Editor
{
    [CustomEditor(typeof(KcpTransport), true)]
    public class KcpTransportEditor : UnityEditor.Editor
    {
        /* Settings. */
        protected SerializedProperty _noDelay;
        protected SerializedProperty _mtu;
        protected SerializedProperty _receiveBufferSize;
        protected SerializedProperty _sendBufferSize;

        /* Server. */
        protected SerializedProperty _bindAddressIPv4;
        protected SerializedProperty _enableIPv6;
        protected SerializedProperty _bindAddressIPv6;
        protected SerializedProperty _port;

        /* Client. */
        protected SerializedProperty _clientAddress;

        /* Advanced. */
        protected SerializedProperty _fastResend;
        protected SerializedProperty _congestionWindow;
        protected SerializedProperty _receiveWindowSize;
        protected SerializedProperty _sendWindowSize;
        protected SerializedProperty _maxRetransmits;

        protected virtual void OnEnable()
        {
            _noDelay = serializedObject.FindProperty(nameof(_noDelay));
            _mtu = serializedObject.FindProperty(nameof(_mtu));
            _receiveBufferSize = serializedObject.FindProperty(nameof(_receiveBufferSize));
            _sendBufferSize = serializedObject.FindProperty(nameof(_sendBufferSize));
            
            _bindAddressIPv4 = serializedObject.FindProperty(nameof(_bindAddressIPv4));
            _enableIPv6 = serializedObject.FindProperty(nameof(_enableIPv6));
            _bindAddressIPv6 = serializedObject.FindProperty(nameof(_bindAddressIPv6));
            _port = serializedObject.FindProperty(nameof(_port));
            
            _clientAddress = serializedObject.FindProperty(nameof(_clientAddress));
            
            _fastResend = serializedObject.FindProperty(nameof(_fastResend));
            _congestionWindow = serializedObject.FindProperty(nameof(_congestionWindow));
            _receiveWindowSize = serializedObject.FindProperty(nameof(_receiveWindowSize));
            _sendWindowSize = serializedObject.FindProperty(nameof(_sendWindowSize));
            _maxRetransmits = serializedObject.FindProperty(nameof(_maxRetransmits));
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var transport = (KcpTransport)target;

            GUI.enabled = false;
            EditorGUILayout.ObjectField("Script:", MonoScript.FromMonoBehaviour(transport), typeof(KcpTransport), false);
            GUI.enabled = true;

            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_noDelay);
            EditorGUILayout.PropertyField(_mtu, new GUIContent("MTU"));
            EditorGUILayout.PropertyField(_receiveBufferSize);
            EditorGUILayout.PropertyField(_sendBufferSize);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space();

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

            EditorGUILayout.LabelField("Advanced", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_fastResend);
            EditorGUILayout.PropertyField(_congestionWindow);
            EditorGUILayout.PropertyField(_receiveWindowSize);
            EditorGUILayout.PropertyField(_sendWindowSize);
            EditorGUILayout.PropertyField(_maxRetransmits);
            EditorGUI.indentLevel--;

            serializedObject.ApplyModifiedProperties();
        }
    }
}