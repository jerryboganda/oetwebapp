import Toastify from "toastify-js";

const toast = {
  success: (text = "") => {
    Toastify({
      text: text,
      duration: 1000,
      position: "right",
      style: {
        background: "rgb(var(--success),1)",
      },
    }).showToast();
  },
};

export default toast;
