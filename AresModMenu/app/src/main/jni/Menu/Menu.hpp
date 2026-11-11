#ifndef MENU_HPP
#define MENU_HPP

#include "Jni.hpp"

void Init(JNIEnv *env, jobject thiz, jobject ctx, jobject title, jobject subtitle);

void setText(JNIEnv *env, jobject obj, const char* text);

jstring Icon(JNIEnv *env, jobject thiz);

jstring IconWebViewData(JNIEnv *env, jobject thiz);

jobjectArray SettingsList(JNIEnv *env, jobject activityObject);

jobjectArray GetFeatureList(JNIEnv *env, jobject context);

// 在 Unity 主线程上执行 IL2CPP 反射探针（只能在主线程调，见 Setup.cpp 里 RegisterMenu 的注释）
void ProbeOnMain(JNIEnv *env, jclass clazz);

jstring NativeQuery(JNIEnv *env, jclass clazz, jint op, jint a1, jint a2, jlong aL, jstring str);

void Changes(JNIEnv *env, jclass clazz, jobject obj,
             jint featNum, jstring featName, jint value, jlong Lvalue,
             jboolean boolean, jstring str);

#endif