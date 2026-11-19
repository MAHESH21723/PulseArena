# Pulse Arena: Online Setup (free, no bills)

Pulse Arena has two ways to play together:

1. **Local Wi-Fi** (works today, nothing to set up). One phone hosts, friends on the same Wi-Fi join.
2. **Internet** (Epic Online Services, free). Needs the one-time setup below, AND an EOS peer-to-peer transport for Netcode that does not exist off the shelf yet (see "Status").

Unity's paid Relay/Multiplayer Services package has been removed, so Unity cannot bill you.

## Status (read this first)

- Local Wi-Fi: complete.
- Internet: **coming soon** (not part of the v0.3.0 release). The lobby/login code is a skeleton and the game shows "Internet unavailable" until the missing piece (a Netcode transport over EOS P2P) is built. The only public one (linkoid/EpicTransport-for-UnityNGO) was last updated in February 2024, targets old Netcode and has no Android evidence. Plan on writing a small transport, or ask for it as the next task.

## A. Local Wi-Fi play on two phones

1. Install the same build on both phones.
2. Connect both phones to the same Wi-Fi network (a phone hotspot also works).
3. Phone 1: choose "Local Wi-Fi", tap Host. A 6-character code appears.
4. Phone 2: choose "Local Wi-Fi". Host appears in the Nearby Rooms list within a couple of seconds; tap it. Or type the 6-character code.
5. If nothing appears: some routers block device-to-device traffic ("AP/client isolation", common on guest Wi-Fi). Turn it off, or use a phone hotspot.
6. The code only works by typing if both phones share the first two numbers of their IP (normal for home routers, e.g. 192.168.x.x). The nearby list does not have this limit.

Android permissions (INTERNET, ACCESS_NETWORK_STATE, ACCESS_WIFI_STATE, CHANGE_WIFI_MULTICAST_STATE) are added automatically at build time.

## B. Internet play setup (Epic Online Services)

1. Go to https://dev.epicgames.com/portal and sign in or create a free Epic account.
2. Create an Organization, then a Product named "Pulse Arena".
3. In the product, open Product Settings. Copy: Product ID, Sandbox ID, Deployment ID (use the Live or Dev deployment).
4. Open Product Settings > Clients > Add New Client. Create a Client Policy of type "Peer 2 Peer" or "Game Client" that allows the Connect, Lobby and P2P features. Copy the Client ID and Client Secret.
5. Open Product Settings > Encryption Key (or "Generate") and copy a 64-character hex key.
6. Open the file `.env` in the project folder and fill:
   `EOS_PRODUCT_ID`, `EOS_SANDBOX_ID`, `EOS_DEPLOYMENT_ID`, `EOS_CLIENT_ID`, `EOS_CLIENT_SECRET`, `EOS_ENCRYPTION_KEY`.
   Never commit `.env`. (Epic's client ID/secret are embedded in the shipped game by design; keeping them out of git is still right.)
7. In Unity: Window > Package Manager > + > "Install package from git URL" and paste the EOS Plugin URL:
   `https://github.com/EOS-Contrib/eos_plugin_for_unity.git#stable` (or download `com.playeveryware.eos-6.3.0.tgz` from the plugin's GitHub Releases and use "Install package from tarball").
8. Install the Epic transport for Netcode once one is available (see Status).
9. Project Settings > Player > Android > Other Settings > Scripting Define Symbols: add `PULSE_EOS`.
10. Menu "Pulse Arena > Write EOS config from .env" (this also runs automatically before every build). It writes `Assets/StreamingAssets/EOS/EpicOnlineServicesConfig.json`, which is git-ignored.
11. In the EOS portal, mark the app's players as using Device ID login (no Epic account needed). Build and test with two phones on different networks (one on mobile data).

## C. Costs (verified)

- Epic Online Services: free. Epic's own site states "no royalty or hosting fees, no cap on the number of titles, and no limit on player accounts" (https://onlineservices.epicgames.com/llms.txt, see also https://onlineservices.epicgames.com/licensing). Lobbies and P2P (including relay) are part of the free Game Services. Deeper Easy Anti-Cheat tiers need a paid agreement (not used here). Epic reserves the right to apply rate limits and can terminate access for breaching the Developer Agreement or one year of non-use: https://onlineservices.epicgames.com/services/terms/agreements
- Peer-to-peer means players' phones carry the traffic; there is no server for you to pay for.
- Local Wi-Fi: no servers at all.
- Unity: nothing is billed once `com.unity.services.multiplayer` is removed and the Unity Cloud project is unlinked.
- Separate: Google Play charges a one-time $25 developer registration fee.
