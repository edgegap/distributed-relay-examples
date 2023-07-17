// parse session_authorization_token and user_authorization_token from command line args.
// mac: "open mirror.app --args session_id=123 user_authorization_token=456"
using System;
using UnityEngine;

namespace Edgegap
{
    public class RelayCredentialsFromArgs : MonoBehaviour
    {
        void Awake()
        {
            String cmd = Environment.CommandLine;

            // parse session_id via regex
            String sessionAuthorizationToken = EdgegapTransport.ReParse(cmd, "session_authorization_token=(\\d+)", "111111");
            String userAuthorizationToken = EdgegapTransport.ReParse(cmd, "user_authorization_token=(\\d+)", "222222");
            Debug.Log($"Parsed sessionAuthorizationToken: {sessionAuthorizationToken} user_authorization_token: {userAuthorizationToken}");

            // configure transport
            EdgegapTransport transport = GetComponent<EdgegapTransport>();
            transport.sessionAuthorizationToken = UInt32.Parse(sessionAuthorizationToken);
            transport.userAuthorizationToken = UInt32.Parse(userAuthorizationToken);
        }
    }
}
