/*
 * Mega Robo Rally - ESP32-S3 player station (Freenove FNK0104S, 4.0" 320x480 capacitive touch)
 *
 * One station per seat. Polls the game server's read-only /api/state, shows this seat's hand,
 * and sends the same plain-GET commands the phone UI uses (/api/player/..., /api/setup/select/...).
 * See documents/ESP32_PLAYER_STATION_DESIGN.md and esp32-player-station/README.md.
 *
 * Landscape 480x320 (the 320x480 panel, rotated), no LVGL: the screen is drawn straight with
 * TFT_eSPI, so the only libraries are TFT_eSPI + FT6336U (both from Freenove's
 * Libraries/FNK0104S) and ArduinoJson 7.
 */

#include <WiFi.h>
#include <HTTPClient.h>
#include <Preferences.h>
#include <ArduinoJson.h>
#include <TFT_eSPI.h>
#include <FT6336U.h>

#include "config.h"   // copy config.example.h to config.h and edit (config.h is git-ignored)

// ---- Board pins (FNK0104S touch controller; TFT pins live in the TFT_eSPI setup) --------------
#define I2C_SCL   15
#define I2C_SDA   16
#define INT_N_PIN 17
#define RST_N_PIN 18

// Landscape. The panel itself is 320x480 portrait; setRotation() turns it. SCREEN_ROTATION is
// 1 or 3 (180 degrees apart, set in config.h) -- pick whichever puts the USB-C port where you want.
#ifndef SCREEN_ROTATION
#define SCREEN_ROTATION 1
#endif
static const int SCREEN_W = 480;
static const int SCREEN_H = 320;

TFT_eSPI tft = TFT_eSPI(320, 480);   // the panel's native (portrait) size
FT6336U  ctp(I2C_SDA, I2C_SCL, RST_N_PIN, INT_N_PIN);
Preferences prefs;

// ---- Game constants (match MRR.Contracts / the phone UI) ---------------------------------------
static const int MAX_DEALT = 12;
static const int NUM_REGS  = 5;
static const int MAX_LIST  = 9;

// MoveCardTypes: 0 blank, 1 U-Turn, 2 Right, 3 Left, 4 Back 1, 5 Fwd 1, 6 Fwd 2, 7 Fwd 3,
//                8 Again, 9 Power Up, 10 Spam, 11 Haywire
static const char* CARD_LETTER[12] = {"-", "U", "R", "L", "B", "1", "2", "3", "A", "P", "S", "H"};
static const char* CARD_NAME[12]   = {"", "U-Turn", "Right", "Left", "Back 1", "Fwd 1", "Fwd 2", "Fwd 3",
                                      "Again", "Power Up", "Spam", "Haywire"};
static const int   DIR_DEG[5]      = {0, 0, 90, 180, 270};   // index = Direction 1 Up,2 Right,3 Down,4 Left

// ---- Layout (landscape 480x320) ----------------------------------------------------------------
// Every touch target comes from one of the rect functions below, used by both drawing and
// hit-testing, so the two can't drift apart.
static const int HEADER_H = 30, STATUS_Y = 30, STATUS_H = 18;
static const int MSG_Y = 50, MSG_H = 34;
static const int REG_Y = 88, REG_W = 90, REG_H = 60, REG_GAP = 6, REG_X0 = 3;
static const int HAND_Y = 152, HAND_W = 76, HAND_H = 56, HAND_GAP = 4, HAND_X0 = 2, HAND_COLS = 6;
static const int BAR_Y = 274, BAR_H = 44;

struct Rect { int x, y, w, h; };
static Rect regRect(int i)      { return {REG_X0 + i * (REG_W + REG_GAP), REG_Y, REG_W, REG_H}; }
static Rect handRect(int i)     { return {HAND_X0 + (i % HAND_COLS) * (HAND_W + HAND_GAP),
                                          HAND_Y + (i / HAND_COLS) * (HAND_H + HAND_GAP), HAND_W, HAND_H}; }
static Rect msgRect()           { return {2, MSG_Y, SCREEN_W - 4, MSG_H}; }
static Rect arrowRect()         { return {4, BAR_Y, 60, BAR_H}; }
static Rect setDirRect()        { return {70, BAR_Y, 160, BAR_H}; }
static Rect shutDownRect()      { return {240, BAR_Y, 236, BAR_H}; }
static Rect seatRect(int i)     { return {6 + (i % 3) * 158, 60 + (i / 3) * 130, 150, 120}; }
static Rect setupBodyRect(int i)  { return {4 + (i % 2) * 114, 70 + (i / 2) * 44, 110, 40}; }
static Rect setupStartRect(int i) { return {244 + (i % 2) * 118, 70 + (i / 2) * 44, 114, 40}; }

// ---- Data model --------------------------------------------------------------------------------
struct Body { int id; String name; uint16_t color; uint16_t fg; };

struct View {
  bool gotData = false;
  int  gamestate = 0;
  // my robot
  bool hasMe = false;
  int  robotId = 0;
  String name, status, flags, msg;
  uint16_t color = 0, fg = 0xFFFF, statusColor = 0;
  int  dealt[MAX_DEALT]; int nDealt = 0;
  int  played[NUM_REGS];
  int  positionValid = 0, dir = 1, dirAdj = 1, shutDown = 0;
  // setup (gamestate 1)
  int  playerToSelect = 0;
  Body bodies[MAX_LIST]; int nBodies = 0;
  int  starts[MAX_LIST]; int nStarts = 0;
};

View view;
int  seat = 0;                    // 0 = not chosen yet (first-boot picker)
int  pendingDir = 1;
unsigned long lastDirTap = 0;
int  setupBody = 0;               // RobotBodyID tapped during setup, until a start position commits it
int  failCount = 0;
bool serverEverSeen = false;
String lastSig;
bool forceRedraw = true;
bool pollNow = true;
unsigned long lastPoll = 0;

// ---- Small helpers -----------------------------------------------------------------------------
static String baseUrl() { return String("http://") + SERVER_HOST + ":" + SERVER_PORT; }

static uint16_t hexTo565(const char* s, uint16_t fallback) {
  if (!s) return fallback;
  if (*s == '#') s++;
  if (strlen(s) < 6) return fallback;
  uint32_t v = strtoul(s, nullptr, 16);
  return tft.color565((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
}

static int parseCsv(const char* s, int* out, int maxN) {
  int n = 0;
  if (!s) return 0;
  while (*s && n < maxN) {
    while (*s == ' ' || *s == ',') s++;
    if (!*s) break;
    out[n++] = atoi(s);
    while (*s && *s != ',') s++;
  }
  return n;
}

static uint16_t cardColor(int t) {
  switch (t) {
    case 1:  return tft.color565(150, 90, 200);   // U-Turn
    case 2:  case 3: return tft.color565(70, 130, 220);    // turns
    case 4:  return tft.color565(230, 140, 40);   // back
    case 5:  case 6: case 7: return tft.color565(70, 180, 90);  // forward
    case 8:  return tft.color565(170, 170, 170);  // again
    case 9:  return tft.color565(240, 220, 60);   // power up
    case 10: return tft.color565(210, 60, 60);    // spam
    case 11: return tft.color565(200, 60, 180);   // haywire
    default: return tft.color565(60, 60, 60);     // blank
  }
}

static uint16_t cardText(int t) {
  return (t == 8 || t == 9 || t == 5 || t == 6 || t == 7 || t == 4) ? TFT_BLACK : TFT_WHITE;
}

// ---- Networking --------------------------------------------------------------------------------
static bool httpGet(const String& path, String* body) {
  if (WiFi.status() != WL_CONNECTED) return false;
  HTTPClient http;
  http.setConnectTimeout(2000);
  http.setTimeout(2500);
  if (!http.begin(baseUrl() + path)) return false;
  int code = http.GET();
  bool ok = (code == 200);
  if (ok && body) *body = http.getString();
  http.end();
  return ok;
}

// Fire-and-forget action (card tap, direction, ...). The next poll reconciles with the server.
static void sendAction(const String& path) {
  httpGet(path, nullptr);
  pollNow = true;
}

static bool parseState(const String& body) {
  JsonDocument filter;
  filter["gamestate"] = true;
  JsonObject fr = filter["robots"][0].to<JsonObject>();
  const char* keys[] = {"RobotID", "RobotName", "RobotColor", "RobotColorFG", "StatusColor", "PlayerStatus",
                        "PositionValid", "Dir", "ShutDown", "PlayerSeat", "FlagEnergyCards",
                        "DirectionAdjustment", "CardsDealt", "CardsPlayed", "PlayerMsg"};
  for (const char* k : keys) fr[k] = true;
  filter["GameConfig"]["PlayerToSelect"] = true;
  filter["GameConfig"]["AvailableRobots"][0]["RobotBodyID"] = true;
  filter["GameConfig"]["AvailableRobots"][0]["Name"] = true;
  filter["GameConfig"]["AvailableRobots"][0]["Color"] = true;
  filter["GameConfig"]["AvailableRobots"][0]["ColorFG"] = true;
  filter["GameConfig"]["AvailableStartPositions"] = true;

  JsonDocument doc;
  DeserializationError err = deserializeJson(doc, body, DeserializationOption::Filter(filter));
  if (err) {
    Serial.printf("JSON error: %s\n", err.c_str());
    return false;
  }

  View nv;
  nv.gotData = true;
  nv.gamestate = doc["gamestate"] | 0;

  for (JsonObject r : doc["robots"].as<JsonArray>()) {
    if ((r["PlayerSeat"] | 0) != seat || seat == 0) continue;
    nv.hasMe = true;
    nv.robotId = r["RobotID"] | 0;
    nv.name = String(r["RobotName"] | "");
    nv.color = hexTo565(r["RobotColor"] | "", TFT_DARKGREY);
    nv.fg = hexTo565(r["RobotColorFG"] | "", TFT_WHITE);
    nv.statusColor = hexTo565(r["StatusColor"] | "", TFT_DARKGREY);
    nv.status = String(r["PlayerStatus"] | "");
    nv.flags = String(r["FlagEnergyCards"] | "");
    nv.msg = String(r["PlayerMsg"] | "");
    if (nv.msg == "undefined") nv.msg = "";
    nv.positionValid = r["PositionValid"] | 0;
    nv.dir = r["Dir"] | 1;
    if (nv.dir < 1 || nv.dir > 4) nv.dir = 1;
    nv.dirAdj = r["DirectionAdjustment"] | 1;
    if (nv.dirAdj < 1 || nv.dirAdj > 4) nv.dirAdj = 1;
    nv.shutDown = r["ShutDown"] | 0;
    nv.nDealt = parseCsv(r["CardsDealt"] | "", nv.dealt, MAX_DEALT);
    for (int i = 0; i < NUM_REGS; i++) nv.played[i] = 0;
    int tmp[NUM_REGS + 2];
    int n = parseCsv(r["CardsPlayed"] | "", tmp, NUM_REGS);
    for (int i = 0; i < n && i < NUM_REGS; i++) nv.played[i] = tmp[i];
    break;
  }

  JsonObject gc = doc["GameConfig"];
  if (!gc.isNull()) {
    nv.playerToSelect = gc["PlayerToSelect"] | 0;
    for (JsonObject b : gc["AvailableRobots"].as<JsonArray>()) {
      if (nv.nBodies >= MAX_LIST) break;
      Body& d = nv.bodies[nv.nBodies++];
      d.id = b["RobotBodyID"] | 0;
      d.name = String(b["Name"] | "");
      d.color = hexTo565(b["Color"] | "", TFT_DARKGREY);
      d.fg = hexTo565(b["ColorFG"] | "", TFT_WHITE);
    }
    for (int p : gc["AvailableStartPositions"].as<JsonArray>()) {
      if (nv.nStarts >= MAX_LIST) break;
      nv.starts[nv.nStarts++] = p;
    }
  }

  view = nv;
  if (millis() - lastDirTap > 1500) pendingDir = view.dir;
  return true;
}

// ---- Drawing -----------------------------------------------------------------------------------
static void drawButton(int x, int y, int w, int h, const String& label, uint16_t bg, uint16_t fg,
                       int font = 2, bool outline = false) {
  tft.fillRoundRect(x, y, w, h, 6, bg);
  if (outline) tft.drawRoundRect(x, y, w, h, 6, TFT_WHITE);
  tft.setTextColor(fg, bg);
  tft.setTextDatum(MC_DATUM);
  tft.drawString(label, x + w / 2, y + h / 2, font);
}

static void drawCard(int x, int y, int w, int h, int type) {
  if (type <= 0 || type > 11) {
    tft.fillRoundRect(x, y, w, h, 6, tft.color565(35, 35, 35));
    tft.drawRoundRect(x, y, w, h, 6, tft.color565(90, 90, 90));
    return;
  }
  uint16_t bg = cardColor(type), fg = cardText(type);
  tft.fillRoundRect(x, y, w, h, 6, bg);
  tft.setTextColor(fg, bg);
  tft.setTextDatum(MC_DATUM);
  tft.drawString(CARD_LETTER[type], x + w / 2, y + h / 2 - 8, 4);
  tft.drawString(CARD_NAME[type], x + w / 2, y + h - 11, 2);
}

static void drawArrow(int cx, int cy, int r, int degrees, uint16_t col) {
  float th = degrees * 0.0174533f;
  float c = cosf(th), s = sinf(th);
  float px[4] = {0.0f, -0.7f * r, 0.0f, 0.7f * r};
  float py[4] = {-1.0f * r, 0.7f * r, 0.3f * r, 0.7f * r};
  int X[4], Y[4];
  for (int i = 0; i < 4; i++) {
    X[i] = cx + (int)(px[i] * c - py[i] * s);
    Y[i] = cy + (int)(px[i] * s + py[i] * c);
  }
  tft.fillTriangle(X[0], Y[0], X[1], Y[1], X[2], Y[2], col);
  tft.fillTriangle(X[0], Y[0], X[2], Y[2], X[3], Y[3], col);
}

static void drawWrapped(const String& text, int x, int y, int maxChars, int lines, uint16_t fg, uint16_t bg) {
  tft.setTextColor(fg, bg);
  tft.setTextDatum(TL_DATUM);
  int pos = 0;
  for (int l = 0; l < lines && pos < (int)text.length(); l++) {
    String chunk = text.substring(pos, pos + maxChars);
    tft.drawString(chunk, x, y + l * 16, 2);
    pos += maxChars;
  }
}

static void drawCentered(const String& line1, const String& line2 = "") {
  tft.fillScreen(TFT_BLACK);
  tft.setTextColor(TFT_WHITE, TFT_BLACK);
  tft.setTextDatum(MC_DATUM);
  tft.drawString(line1, SCREEN_W / 2, SCREEN_H / 2 - 14, 4);
  if (line2.length()) tft.drawString(line2, SCREEN_W / 2, SCREEN_H / 2 + 22, 2);
}

static bool canProgram() { return view.gamestate >= 2 && view.gamestate <= 4; }
static bool showDirButtons() { return view.hasMe && view.positionValid == 0; }
static bool showShutDown() { return view.hasMe && view.gamestate == 4; }

static void renderSeatPicker() {
  tft.fillScreen(TFT_BLACK);
  tft.setTextColor(TFT_WHITE, TFT_BLACK);
  tft.setTextDatum(TC_DATUM);
  tft.drawString("Pick this station's seat", SCREEN_W / 2, 20, 4);
  for (int i = 0; i < 6; i++) {
    Rect r = seatRect(i);
    drawButton(r.x, r.y, r.w, r.h, String(i + 1), tft.color565(40, 90, 160), TFT_WHITE, 6);
  }
}

static void renderSetup() {
  tft.fillScreen(TFT_BLACK);
  tft.setTextColor(TFT_WHITE, TFT_BLACK);
  tft.setTextDatum(TL_DATUM);
  tft.drawString("Seat " + String(seat), 8, 8, 4);
  if (view.playerToSelect != seat) {
    tft.drawString("Waiting for Seat " + String(view.playerToSelect) + " to finish choosing...", 8, 60, 2);
    return;
  }
  tft.drawString("Your turn! Pick a robot, then a start position:", 130, 14, 2);
  for (int i = 0; i < view.nBodies; i++) {
    const Body& b = view.bodies[i];
    Rect r = setupBodyRect(i);
    drawButton(r.x, r.y, r.w, r.h, b.name, b.color, b.fg, 2, setupBody == b.id);
  }
  for (int i = 0; i < view.nStarts; i++) {
    Rect r = setupStartRect(i);
    drawButton(r.x, r.y, r.w, r.h, "Start " + String(view.starts[i]), tft.color565(60, 60, 60), TFT_WHITE);
  }
}

static void renderMain() {
  tft.fillScreen(TFT_BLACK);

  // Header: robot name + flag/energy/cards
  tft.fillRect(0, 0, SCREEN_W, HEADER_H, view.color);
  tft.setTextColor(view.fg, view.color);
  tft.setTextDatum(ML_DATUM);
  tft.drawString(view.name, 8, HEADER_H / 2, 4);
  tft.setTextDatum(MR_DATUM);
  tft.drawString(view.flags, SCREEN_W - 8, HEADER_H / 2, 2);

  // Status line
  tft.fillRect(0, STATUS_Y, SCREEN_W, STATUS_H, view.statusColor);
  tft.setTextColor(TFT_BLACK, view.statusColor);
  tft.setTextDatum(MC_DATUM);
  tft.drawString(view.status, SCREEN_W / 2, STATUS_Y + STATUS_H / 2, 2);

  // Message (tap to confirm)
  if (view.msg.length()) {
    Rect m = msgRect();
    tft.fillRoundRect(m.x, m.y, m.w, m.h, 6, tft.color565(255, 240, 150));
    drawWrapped(view.msg, 8, MSG_Y + 1, 56, 2, TFT_BLACK, tft.color565(255, 240, 150));
  }

  // Registers
  for (int i = 0; i < NUM_REGS; i++) {
    Rect r = regRect(i);
    drawCard(r.x, r.y, r.w, r.h, view.played[i]);
  }

  // Hand
  if (canProgram()) {
    for (int i = 0; i < view.nDealt && i < MAX_DEALT; i++) {
      Rect r = handRect(i);
      drawCard(r.x, r.y, r.w, r.h, view.dealt[i]);
    }
  } else {
    tft.setTextColor(TFT_LIGHTGREY, TFT_BLACK);
    tft.setTextDatum(MC_DATUM);
    tft.drawString("Waiting...", SCREEN_W / 2, 210, 4);
  }

  // Bottom bar: direction arrow + Set Direction, Shut Down
  if (showDirButtons()) {
    Rect a = arrowRect(), d = setDirRect();
    int deg = (DIR_DEG[pendingDir] + DIR_DEG[view.dirAdj]) % 360;
    tft.fillRoundRect(a.x, a.y, a.w, a.h, 6, tft.color565(50, 50, 50));
    drawArrow(a.x + a.w / 2, a.y + a.h / 2, 18, deg, TFT_WHITE);
    drawButton(d.x, d.y, d.w, d.h, "Set Direction", tft.color565(128, 0, 128), TFT_WHITE);
  }
  if (showShutDown()) {
    Rect s = shutDownRect();
    bool on = view.shutDown == 2;
    drawButton(s.x, s.y, s.w, s.h, on ? "Cancel Shutdown" : "Shut Down",
               on ? TFT_YELLOW : tft.color565(60, 60, 60), on ? TFT_BLACK : TFT_WHITE);
  }
}

// What would be drawn, as a string: redraw only when it changes (full redraws flicker).
static String signature() {
  if (seat == 0) return "pick";
  if (!view.gotData) return String("wait") + (WiFi.status() == WL_CONNECTED) + failCount;
  if (view.gamestate == 25) return "nogame";
  String s = String(view.gamestate) + "|" + seat + "|" + setupBody + "|";
  if (view.gamestate == 1) {
    s += String(view.playerToSelect);
    for (int i = 0; i < view.nBodies; i++) s += "b" + String(view.bodies[i].id);
    for (int i = 0; i < view.nStarts; i++) s += "s" + String(view.starts[i]);
    return s;
  }
  if (!view.hasMe) return s + "norobot";
  s += view.name + view.status + view.flags + view.msg + view.positionValid + "," + pendingDir + "," +
       view.dirAdj + "," + view.shutDown + "|";
  for (int i = 0; i < NUM_REGS; i++) s += String(view.played[i]) + ",";
  for (int i = 0; i < view.nDealt; i++) s += String(view.dealt[i]) + ",";
  return s;
}

static void render() {
  if (seat == 0) { renderSeatPicker(); return; }
  if (!view.gotData) {
    if (WiFi.status() != WL_CONNECTED) drawCentered("Connecting to WiFi...", String(WIFI_SSID));
    else if (failCount >= 3) drawCentered("Server not reachable", String(SERVER_HOST) + ":" + SERVER_PORT);
    else drawCentered("Connecting...");
    return;
  }
  if (view.gamestate == 25) { drawCentered("No game running", "Seat " + String(seat)); return; }
  if (view.gamestate == 1) { renderSetup(); return; }
  if (!view.hasMe) { drawCentered("Seat " + String(seat), "has no robot in this game"); return; }
  renderMain();
}

// ---- Touch -------------------------------------------------------------------------------------
static bool inRect(int px, int py, const Rect& r) {
  return px >= r.x && px < r.x + r.w && py >= r.y && py < r.y + r.h;
}

static void handleTap(int x, int y) {
  // First boot: seat picker
  if (seat == 0) {
    for (int i = 0; i < 6; i++) {
      if (inRect(x, y, seatRect(i))) {
        seat = i + 1;
        prefs.putInt("seat", seat);
        view = View();
        forceRedraw = true;
        pollNow = true;
      }
    }
    return;
  }
  if (!view.gotData) return;

  // Setup: pick body, then start position
  if (view.gamestate == 1) {
    if (view.playerToSelect != seat) return;
    for (int i = 0; i < view.nBodies; i++)
      if (inRect(x, y, setupBodyRect(i))) { setupBody = view.bodies[i].id; forceRedraw = true; }
    for (int i = 0; i < view.nStarts; i++)
      if (inRect(x, y, setupStartRect(i)) && setupBody) {
        sendAction("/api/setup/select/" + String(seat) + "/" + view.starts[i] + "/" + setupBody);
        setupBody = 0;
      }
    return;
  }
  if (!view.hasMe) return;
  String rid = String(view.robotId);

  // Message banner: confirm
  if (view.msg.length() && inRect(x, y, msgRect())) {
    sendAction("/api/player/3/" + rid);
    return;
  }

  if (canProgram()) {
    // Tap a register: send the card back to the hand
    for (int i = 0; i < NUM_REGS; i++) {
      if (view.played[i] > 0 && inRect(x, y, regRect(i))) {
        view.played[i] = 0;                                  // optimistic; the next poll reconciles
        forceRedraw = true;
        sendAction("/api/player/1/" + rid + "/-1/" + String(i + 1));
        return;
      }
    }
    // Tap a hand card: play it into the first empty register
    for (int i = 0; i < view.nDealt && i < MAX_DEALT; i++) {
      if (inRect(x, y, handRect(i))) {
        int type = view.dealt[i];
        for (int r = 0; r < NUM_REGS; r++) {
          if (view.played[r] == 0) {                          // optimistic
            view.played[r] = type;
            for (int k = i; k < view.nDealt - 1; k++) view.dealt[k] = view.dealt[k + 1];
            view.nDealt--;
            forceRedraw = true;
            break;
          }
        }
        sendAction("/api/player/1/" + rid + "/" + String(type) + "/-1");
        return;
      }
    }
  }

  // Direction arrow / Set Direction
  if (showDirButtons()) {
    if (inRect(x, y, arrowRect())) {
      lastDirTap = millis();
      pendingDir = (pendingDir % 4) + 1;
      forceRedraw = true;
      sendAction("/api/player/4/" + rid + "/" + String(pendingDir));
      return;
    }
    if (inRect(x, y, setDirRect())) {
      sendAction("/api/player/5/" + rid + "/1");
      return;
    }
  }

  // Shut Down toggle
  if (showShutDown() && inRect(x, y, shutDownRect())) {
    sendAction("/api/player/6/" + rid);
    return;
  }
}

// ---- Setup / loop ------------------------------------------------------------------------------
static bool touchDown = false;
static int  touchX = 0, touchY = 0, touchStartX = 0, touchStartY = 0;
static unsigned long touchStart = 0;

// The touch controller reports in the panel's native portrait frame (x 0..319, y 0..479), the
// same frame the display uses at rotation 0. Turn that into landscape screen coordinates for the
// rotation in use. Derived from TFT_eSPI's ST7796 MADCTL settings for rotations 1 and 3
// (MV; MX|MY|MV) -- not yet checked on a real board. If taps land mirrored, flip
// SCREEN_ROTATION in config.h between 1 and 3 (and report it).
static bool mapTouch(int tx, int ty, int& x, int& y) {
  if (tx < 0 || tx >= 320 || ty < 0 || ty >= 480) return false;
#if SCREEN_ROTATION == 3
  x = 479 - ty;
  y = tx;
#else
  x = ty;
  y = 319 - tx;
#endif
  return true;
}

static void pollTouch() {
  FT6336U_TouchPointType tp = ctp.scan();
  if (tp.touch_count > 0) {
    int x, y;
    if (!mapTouch(tp.tp[0].x, tp.tp[0].y, x, y)) return;
    if (!touchDown) {
      touchDown = true;
      touchStart = millis();
      touchStartX = x;
      touchStartY = y;
    }
    touchX = x;
    touchY = y;
    // Factory reset gesture: hold the top-left corner for 5 s to forget the seat
    if (millis() - touchStart > 5000 && touchStartX < 60 && touchStartY < 60) {
      prefs.remove("seat");
      drawCentered("Seat cleared", "Restarting...");
      delay(800);
      ESP.restart();
    }
  } else if (touchDown) {
    touchDown = false;
    handleTap(touchStartX, touchStartY);
  }
}

static void pollServer() {
  String body;
  if (httpGet("/api/state", &body) && parseState(body)) {
    failCount = 0;
    serverEverSeen = true;
  } else if (failCount < 100) {
    failCount++;
    if (failCount >= 3 && view.gotData) { view = View(); forceRedraw = true; }  // show "not reachable"
  }
}

void setup() {
  Serial.begin(115200);

  ctp.begin();
  tft.begin();
  tft.setRotation(SCREEN_ROTATION);   // landscape; touch is remapped in mapTouch()
#ifdef TFT_BL
  pinMode(TFT_BL, OUTPUT);
  digitalWrite(TFT_BL, TFT_BACKLIGHT_ON);
#endif
  tft.fillScreen(TFT_BLACK);

  prefs.begin("mrr", false);
  seat = prefs.getInt("seat", 0);

  WiFi.mode(WIFI_STA);
  WiFi.setAutoReconnect(true);
  WiFi.begin(WIFI_SSID, WIFI_PASS);
}

void loop() {
  pollTouch();

  if (seat != 0 && (pollNow || millis() - lastPoll > POLL_INTERVAL_MS)) {
    pollNow = false;
    lastPoll = millis();
    pollServer();
  }

  String sig = signature();
  if (forceRedraw || sig != lastSig) {
    lastSig = sig;
    forceRedraw = false;
    render();
  }
  delay(15);
}
