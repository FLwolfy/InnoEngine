
#ifndef inno_ui_COMMON_H
#define inno_ui_COMMON_H

#include <stdio.h>
#include <stdint.h>
#include <stddef.h>
/* Calling convention */

#if defined(_WIN32) || defined(_WIN64)
#define inno_ui_CALL __cdecl
#else
#define inno_ui_CALL
#endif

/* API export/import */
#if defined(_WIN32) || defined(_WIN64)
#define inno_ui_EXPORT_INTERNAL __declspec(dllexport)
#ifdef inno_ui_BUILD_SHARED
#define inno_ui_EXPORT __declspec(dllexport)
#else
#define inno_ui_EXPORT __declspec(dllimport)
#endif
#elif defined(__GNUC__) || defined(__clang__)
#define inno_ui_EXPORT_INTERNAL __attribute__((visibility("default")))
#ifdef inno_ui_BUILD_SHARED
#define inno_ui_EXPORT __attribute__((visibility("default")))
#else
#define inno_ui_EXPORT
#endif
#else
#define inno_ui_EXPORT_INTERNAL
#define inno_ui_EXPORT
#endif

#if defined __cplusplus
#define inno_ui_EXTERN extern "C"
#else
#include <stdarg.h>
#include <stdbool.h>
#define inno_ui_EXTERN extern
#endif

#define inno_ui_API(type) inno_ui_EXTERN inno_ui_EXPORT type inno_ui_CALL
#define inno_ui_API_INTERNAL(type) inno_ui_EXTERN inno_ui_EXPORT_INTERNAL type inno_ui_CALL

inno_ui_API(const char*) inno_ui_GetLastError(void);
inno_ui_API(void) inno_ui_ClearLastError(void);

#endif
