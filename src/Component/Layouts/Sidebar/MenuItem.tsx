import React, { Fragment, useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { LinkInterface, MenuItemProps } from "@/interface/common";

const MenuItem: React.FC<MenuItemProps> = ({
  title,
  iconClass,
  type,
  path,
  badgeCount,
  links,
  name,
  collapseId,
}) => {
  const pathname = usePathname();
  const [expandedItems, setExpandedItems] = useState<string[]>([]);

  const isActive = useCallback(
    (linkPath: string) => linkPath === pathname,
    [pathname]
  );

  const checkUnder = useCallback(
    (links: LinkInterface[] | undefined) => {
      return (links || []).some(
        (link: LinkInterface) =>
          isActive(link.path) ||
          (link.children &&
            link.children.some((link: { path: string }) => isActive(link.path)))
      );
    },
    [isActive]
  );

  useEffect(() => {
    if (links) {
      const initiallyExpanded: string[] = [];

      if (checkUnder(links) && collapseId) {
        initiallyExpanded.push(collapseId);
      }

      links.forEach((link) => {
        if (link.children && link.collapseId) {
          if (link.children.some((child) => isActive(child.path))) {
            initiallyExpanded.push(link.collapseId);
          }
        }
      });

      setExpandedItems(initiallyExpanded);
    }
  }, [pathname, links, collapseId, checkUnder, isActive]);

  const toggleCollapse = (id: string) => {
    setExpandedItems((prev) =>
      prev.includes(id) ? prev.filter((item) => item !== id) : [...prev, id]
    );
  };

  const isExpanded = (id: string) => expandedItems.includes(id);

  const IconTag = iconClass;

  return (
    <Fragment>
      {type === "dropdown" ? (
        <Fragment>
          {title && (
            <li className="menu-title">
              <span>{title}</span>
            </li>
          )}
          <li>
            <Link
              href={collapseId ? `#${collapseId}` : ""}
              onClick={(e) => {
                e.preventDefault();
                if (collapseId) toggleCollapse(collapseId);
              }}
              aria-expanded={isExpanded(collapseId || "")}
            >
              {typeof iconClass === "string" ? (
                <i className={iconClass}></i>
              ) : (
                <IconTag width={21} />
              )}
              {name}
              {badgeCount && (
                <span
                  className={`badge ${
                    collapseId === "advance-ui"
                      ? "rounded-pill bg-warning"
                      : badgeCount === "new"
                        ? "text-light-success"
                        : "text-primary-dark bg-primary-300"
                  } badge-notification ms-2`}
                >
                  {badgeCount}
                </span>
              )}
            </Link>
            {links && (
              <ul
                className={`collapse ${isExpanded(collapseId || "") ? "show" : ""}`}
                id={collapseId}
              >
                {(links || []).map((link, index: number) => (
                  <Fragment key={index}>
                    {link.children ? (
                      <li className="another-level">
                        <Link
                          href={`#${link.collapseId}`}
                          onClick={(e) => {
                            e.preventDefault();
                            if (link.collapseId)
                              toggleCollapse(link.collapseId);
                          }}
                          aria-expanded={isExpanded(link.collapseId || "")}
                        >
                          {link.name}
                        </Link>
                        <ul
                          className={`collapse ${
                            isExpanded(link.collapseId || "") ? "show" : ""
                          }`}
                          id={link.collapseId}
                        >
                          {link.children.map((underLink, index) => (
                            <li
                              key={index}
                              className={
                                isActive(underLink.path) ? "active" : ""
                              }
                            >
                              <Link href={underLink.path}>
                                {underLink.name}
                              </Link>
                            </li>
                          ))}
                        </ul>
                      </li>
                    ) : (
                      <li className={isActive(link.path) ? "active" : ""}>
                        <Link href={link.path}>{link.name}</Link>
                      </li>
                    )}
                  </Fragment>
                ))}
              </ul>
            )}
          </li>
        </Fragment>
      ) : (
        <li className="no-sub">
          <Link href={path || ""}>
            {typeof iconClass === "string" ? (
              <i className={iconClass}></i>
            ) : (
              <IconTag />
            )}
            {name}
          </Link>
        </li>
      )}
    </Fragment>
  );
};

export default MenuItem;
