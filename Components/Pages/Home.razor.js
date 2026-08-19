// The console panel follows the newest line. Home.razor calls this after any render that
// added output, rather than on every poll, so a log that has not changed does not yank the
// panel back down while it is being read.
export function scrollToBottom(element) {
    if (element) {
        element.scrollTop = element.scrollHeight;
    }
}
