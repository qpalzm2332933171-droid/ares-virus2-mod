#!/bin/bash
# 手工打包 LSPosed 模块 APK（不依赖 gradle）
set -e
ROOT=/d/arsbd/AresModMenu
SRC=$ROOT/app/src/main
OUT=$ROOT/build
SDK=/d/Android/AndroidSDK
BT=$SDK/build-tools/36.0.0
AJAR=$SDK/platforms/android-36/android.jar
JAVAC=$(ls "/c/Program Files/Java/latest/bin/javac.exe" 2>/dev/null || ls "/c/Program Files/Java/jdk-25"*/bin/javac.exe 2>/dev/null | head -1)
[ -z "$JAVAC" ] && JAVAC=javac

rm -rf "$OUT"
mkdir -p "$OUT/compiled" "$OUT/gen" "$OUT/classes" "$OUT/dex" "$OUT/stubjar"

echo "== 1) 编译 Xposed stub 成 jar =="
"$JAVAC" -nowarn -d "$OUT/stubjar" -cp "$AJAR" \
    $(find "$ROOT/stubs" -name "*.java") 2>&1 | tail -5
(cd "$OUT/stubjar" && jar cf "$OUT/xposed-stub.jar" .)

echo "== 2) aapt2 compile 资源 =="
"$BT/aapt2.exe" compile --dir "$SRC/res" -o "$OUT/compiled/res.zip" 2>&1 | tail -5

echo "== 3) aapt2 link 生成 base.apk + R.java =="
"$BT/aapt2.exe" link -o "$OUT/base.apk" -I "$AJAR" \
    --manifest "$SRC/AndroidManifest.xml" \
    --java "$OUT/gen" \
    --min-sdk-version 21 --target-sdk-version 36 \
    "$OUT/compiled/res.zip" 2>&1 | tail -10

echo "== 4) javac 编译 java =="
"$JAVAC" -nowarn -source 11 -target 11 -classpath "$AJAR:$OUT/xposed-stub.jar" \
    -d "$OUT/classes" \
    $(find "$SRC/java" "$OUT/gen" -name "*.java") 2>&1 | grep -v "^Note"
jrc=${PIPESTATUS[0]}
[ "$jrc" -eq 0 ] || { echo "JAVAC FAILED rc=$jrc"; exit "$jrc"; }

echo "== 5) d8 生成 classes.dex =="
(cd "$OUT/classes" && jar cf "$OUT/classes.jar" .)
"$BT/d8.bat" --release --min-api 21 --lib "$AJAR" --output "$OUT/dex" \
    "$OUT/classes.jar" 2>&1 | tail -10

echo "== 6) 组装 APK =="
cd "$OUT"
cp base.apk unsigned.apk
mkdir -p assets lib/arm64-v8a
cp -rf "$SRC/assets/." assets/ 2>/dev/null || true
cp -f "$SRC/libs/arm64-v8a/libmodmenu.so" assets/libmodmenu.so 2>/dev/null || true
date +%s%N > assets/build_stamp.txt 2>/dev/null || date +%s > assets/build_stamp.txt
cp "$SRC/libs/arm64-v8a/libmodmenu.so" lib/arm64-v8a/ 2>/dev/null || true
[ -d "$ROOT/assets_extra" ] && cp -r "$ROOT/assets_extra/"* assets/ 2>/dev/null || true

# 用 zip 把 dex / assets / lib 塞进 apk
cp -f "$OUT/dex/classes.dex" "$OUT/classes.dex"
(cd "$OUT" && zip -q -r unsigned.apk classes.dex assets lib 2>/dev/null || true)

echo "== 7) zipalign + 签名 =="
"$BT/zipalign.exe" -f -p 4 unsigned.apk aligned.apk
if [ ! -f "$ROOT/ks.jks" ]; then
  keytool -genkeypair -keystore "$ROOT/ks.jks" -storepass ares123 -keypass ares123 \
    -alias ares -keyalg RSA -keysize 2048 -validity 10000 \
    -dname "CN=Ares,OU=Mod,O=Ares,L=X,S=X,C=CN" 2>&1 | tail -3
fi
"$BT/apksigner.bat" sign --ks "$ROOT/ks.jks" --ks-pass pass:ares123 --key-pass pass:ares123 \
    --out "$OUT/AresModMenu.apk" aligned.apk 2>&1 | tail -5

ls -la "$OUT/AresModMenu.apk"
echo "DONE: $OUT/AresModMenu.apk"
