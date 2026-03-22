import React, { useEffect, useState } from "react";
import { MoonStars, SunDim } from "@phosphor-icons/react";

const HeaderMode = () => {
  const [theme, setTheme] = useState(() => {
    return "light";
  });
  useEffect(() => {
    if (typeof window !== "undefined") {
      const storedTheme = localStorage.getItem("theme-mode") || "light";
      setTheme(storedTheme);
      document.body.classList.remove("light", "dark");
      document.body.classList.add(storedTheme);
    }
  }, []);

  useEffect(() => {
    if (typeof window !== "undefined") {
      document.body.classList.remove("light", "dark");
      document.body.classList.add(theme);
      localStorage.setItem("theme-mode", theme);
    }
  }, [theme]);

  const toggleTheme = () => {
    const newTheme = theme === "light" ? "dark" : "light";
    setTheme(newTheme);
    document.body.classList.remove("light", "dark");
    document.body.classList.add(newTheme);
    localStorage.setItem("theme-mode", newTheme);
  };

  return (
    <>
      <div className="header-dark" onClick={toggleTheme}>
        <div className={`sun-logo head-icon ${theme === "dark" ? "sun" : ""}`}>
          <MoonStars size={24} />
        </div>
        <div
          className={`moon-logo head-icon ${theme === "dark" ? "moon" : ""}`}
        >
          <SunDim size={24} />
        </div>
      </div>
    </>
  );
};

export default HeaderMode;
