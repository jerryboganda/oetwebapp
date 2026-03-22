import { useEffect, useState } from "react";
import { IconArrowUp } from "@tabler/icons-react";

const TopGo = () => {
  const [scrollValue, setScrollValue] = useState(0);
  const [showGoTop, setShowGoTop] = useState(false);

  useEffect(() => {
    const handleScroll = () => {
      const pos = window.scrollY;
      const calcHeight = document.body.scrollHeight - window.innerHeight;
      const newScrollValue = Math.round((pos * 100) / calcHeight);
      setScrollValue(newScrollValue);
      setShowGoTop(pos > 100);
    };

    window.addEventListener("scroll", handleScroll);
    handleScroll();
    return () => {
      window.removeEventListener("scroll", handleScroll);
    };
  }, []);

  const handleClick = () => {
    window.scrollTo({
      top: 0,
      behavior: "smooth",
    });
  };

  return (
    <div
      className="go-top"
      style={{
        display: showGoTop ? "grid" : "none",
        background: `conic-gradient( rgba(var(--info),1),rgba(var(--primary),1),rgba(var(--danger),1),rgba(var(--info-dark),1),rgba(var(--primary-dark),1),rgba(var(--danger-dark),1),  ${scrollValue}%, rgba(var(--primary),.3) ${scrollValue}%)`,
      }}
      onClick={handleClick}
    >
      <span className="progress-value">
        <span className="d-flex align-items-center justify-content-center">
          <IconArrowUp size={24} />
        </span>
      </span>
    </div>
  );
};

export default TopGo;
