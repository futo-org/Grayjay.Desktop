#ifndef BLINK_WINMAP_H_
#define BLINK_WINMAP_H_
#include <stddef.h>
#include <sys/types.h>
#if defined(__CYGWIN__) && defined(BLINK_CYGWIN_LINEAR)
void *WindowsMmap(void *, size_t, int, int, int, off_t);
int WindowsOwnsMap(void *);
int WindowsMunmap(void *, size_t);
int WindowsMprotect(void *, size_t, int);
int WindowsRawMprotect(void *, size_t, int);
#endif
#endif
