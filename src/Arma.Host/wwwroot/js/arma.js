// Sunucuda üretilen dosyayı (CSV gibi) tarayıcıda indirir.
window.armaDownload = (name, type, base64) => {
  const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
  const url = URL.createObjectURL(new Blob([bytes], { type }));
  const link = Object.assign(document.createElement('a'), { href: url, download: name });
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
};

// Kamerayla karekod okuma (etkinlik girişi). Tarayıcının yerleşik BarcodeDetector'ını kullanır;
// desteklemeyen tarayıcıda false döner ve ekran elle/okuyucuyla kod girişine kalır.
window.armaScanner = {
  supported: () => 'BarcodeDetector' in window && !!navigator.mediaDevices?.getUserMedia,
  start: async (video, dotnet) => {
    if (!window.armaScanner.supported()) return false;
    const detector = new BarcodeDetector({ formats: ['qr_code'] });
    const stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' } });
    video.srcObject = stream; await video.play();
    let last = '', lastAt = 0, running = true;
    video._armaStop = () => { running = false; stream.getTracks().forEach(t => t.stop()); };
    const tick = async () => {
      if (!running) return;
      try {
        const codes = await detector.detect(video);
        const value = codes[0]?.rawValue;
        // Aynı bilet kamerada dururken tekrar tekrar okunmasın.
        if (value && (value !== last || Date.now() - lastAt > 5000)) {
          last = value; lastAt = Date.now();
          await dotnet.invokeMethodAsync('OnScanned', value);
        }
      } catch { }
      setTimeout(tick, 300);
    };
    tick();
    return true;
  },
  stop: video => video?._armaStop?.(),
};
