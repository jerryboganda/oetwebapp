import React from "react";

interface BreadcrumbsProps {
  title: string;
  mainTitle: string;
  path: string[];
  Icon?: React.ElementType;
}

const Breadcrumbs: React.FC<BreadcrumbsProps> = ({
  title,
  mainTitle,
  path,
  Icon,
}) => {
  return (
    <div className="row m-1">
      <div className="col-12">
        <h4 className="main-title">{mainTitle}</h4>
        <ul className="app-line-breadcrumbs mb-3">
          <li>
            <a href="#" className="f-s-14 f-w-500">
              <span>
                {Icon && (
                  <Icon
                    width={16}
                    height={16}
                    className="f-s-16"
                    // Add type assertion to handle both Phosphor and standard SVG icons
                    {...({ weight: "duotone" } as any)}
                  />
                )}
                {title}
              </span>
            </a>
          </li>
          {path.map((item, index) => (
            <li
              key={index}
              className={index === path.length - 1 ? "active" : ""}
            >
              {index === path.length - 1 ? (
                <a href="#" className="f-s-14 f-w-500">
                  {item}
                </a>
              ) : (
                <span className="f-s-14 f-w-500">{item}</span>
              )}
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
};

export default Breadcrumbs;
